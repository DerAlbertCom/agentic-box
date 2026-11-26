using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Build;

/// <summary>What to build.</summary>
/// <param name="Teams">The teams to build, each with the squads a team build makes.</param>
/// <param name="RootDirectory">The discovery start directory, used when a team has no recorded owning project root.</param>
/// <param name="Tag">The tag the builds are stored under.</param>
/// <param name="Rebuild">Whether every component image is built again, changed or not.</param>
internal sealed record BuildRequest(
    IReadOnlyList<SquadResolver.ResolvedTeam> Teams,
    string RootDirectory,
    string Tag,
    bool Rebuild = false
);

/// <summary>
/// Builds the squads of the teams: resolves each squad, pins its base image and registry
/// kits by digest, makes one image per component (workload, local kits, state mixin,
/// config mixin) in the host image store under its final registry name, and keeps the
/// build record. A component whose inputs did not change is not built again.
/// </summary>
internal sealed class BuildService(
    SquadResolver resolver,
    WorkloadKitGenerator workloadGenerator,
    StateMixinGenerator stateGenerator,
    BuildStore store,
    ComponentImages images,
    IRegistryDigests registry,
    KitBuildStep kitBuildStep,
    IYamlParser parser,
    IConsole console
)
{
    /// <summary>
    /// Builds the squads of the teams and returns the exit code of the command. Every squad
    /// is resolved and checked before Docker is used or a build changes.
    /// </summary>
    public async Task<int> BuildAsync(BuildRequest request, CancellationToken cancellationToken)
    {
        RegistryImageName.ValidateTag(request.Tag);
        var squads = request
            .Teams.SelectMany(team =>
                team.Squads.Select(squad =>
                    (
                        Team: team,
                        Squad: resolver.Resolve(
                            team.RootDirectory ?? request.RootDirectory,
                            team.Name,
                            squad
                        )
                    )
                )
            )
            .ToList();
        foreach (var (team, squad) in squads)
        {
            Repository(squad, new SquadBuildIdentity(team.Name, squad.Name, request.Tag));
        }

        var imageStore = await images.ImageStoreAsync(cancellationToken);
        foreach (var (team, squad) in squads)
        {
            await BuildSquadAsync(team, squad, request, imageStore, cancellationToken);
        }

        return 0;
    }

    private async Task BuildSquadAsync(
        SquadResolver.ResolvedTeam team,
        ResolvedSquad squad,
        BuildRequest request,
        string imageStore,
        CancellationToken cancellationToken
    )
    {
        var identity = new SquadBuildIdentity(team.Name, squad.Name, request.Tag);
        var buildDirectory = store.DirectoryFor(identity);
        var previous = Previous(identity);
        var target = new ComponentTarget(
            imageStore,
            Repository(squad, identity),
            Path.Combine(buildDirectory, "metadata"),
            previous?.Components ?? [],
            request.Rebuild
        );
        var built = new List<BuildComponent>();
        var anyBuilt = false;

        async Task<BuildComponent> MakeAsync(
            string name,
            string inputHash,
            Func<Task<ComponentSource>> source
        )
        {
            var component = await images.ReusableAsync(target, name, inputHash, cancellationToken);
            if (component is null)
            {
                component = await images.BuildAsync(target, await source(), cancellationToken);
                anyBuilt = true;
            }
            built.Add(component);
            return component;
        }

        var baseImage = await PinAsync(squad.Workload.Template, cancellationToken);
        var workloadDirectory = Path.Combine(buildDirectory, WorkloadKitGenerator.KitName);
        workloadGenerator.Generate(squad.Workload, workloadDirectory, PinnedReference(baseImage));
        var workloadSource = new ComponentSource(
            ComponentNames.Workload,
            ComponentKind.Workload,
            workloadDirectory,
            Path.Combine(workloadDirectory, WorkloadKitGenerator.KitName + ".yaml"),
            new Dictionary<string, string>(),
            WithGitHubToken: true
        );
        await MakeAsync(
            ComponentNames.Workload,
            workloadSource.InputHash,
            () => Task.FromResult(workloadSource)
        );

        var kits = new List<ManifestKit>();
        foreach (var kit in squad.Kits)
        {
            kits.Add(
                kit.IsLocal
                    ? await LocalKitAsync(kit, buildDirectory, MakeAsync, cancellationToken)
                    : await RegistryKitAsync(kit, cancellationToken)
            );
        }

        var stateDirectory = Path.Combine(buildDirectory, StateMixinGenerator.MixinName);
        if (squad.State.Count > 0)
        {
            stateGenerator.Generate(squad.State, stateDirectory);
            var stateSource = MixinSource(ComponentNames.State, stateDirectory);
            await MakeAsync(
                ComponentNames.State,
                stateSource.InputHash,
                () => Task.FromResult(stateSource)
            );
        }
        else if (Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }

        var unchanged =
            previous is not null
            && !anyBuilt
            && SameRegistryKits(previous.Kits, kits)
            && previous.Base?.Digest == baseImage.Digest;
        var manifest = Manifest(team.Name, squad, request.Tag, kits, baseImage, imageStore);
        manifest.CreatedAt = unchanged ? previous!.CreatedAt : DateTimeOffset.Now;
        manifest.WorkloadKitDirectory = workloadDirectory;
        manifest.StateMixinDirectory = squad.State.Count > 0 ? stateDirectory : null;
        await ConfigurationAsync(team.Name, squad, manifest, buildDirectory, MakeAsync);

        manifest.Components = built;
        manifest.SourceHashes = BuildSources.Capture(manifest);
        store.Save(manifest);

        console.WriteLine($"Squad '{squad.Name}' built as '{request.Tag}'.");
        console.WriteLine(
            $"Create a sandbox from it with: abox create --team {team.Name} --squad {squad.Name} --tag {request.Tag} --local"
        );
    }

    private delegate Task<BuildComponent> Make(
        string name,
        string inputHash,
        Func<Task<ComponentSource>> source
    );

    private async Task<ManifestKit> LocalKitAsync(
        KitSource kit,
        string buildDirectory,
        Make make,
        CancellationToken cancellationToken
    )
    {
        var name = ComponentNames.Kit(kit.Name);
        if (!RegistryImageName.IsValidRepository(kit.Name.ToLowerInvariant()))
        {
            throw new AboxException(
                $"The kit name '{kit.Name}' cannot name a component image. Use lowercase letters, digits and single separators (., _, -)."
            );
        }

        var descriptor = Descriptor(kit.Directory!);
        EnsureCreatePhaseArguments(kit, descriptor);
        // A kit with a build step is built from its staged copy, so its inputs are its folder
        // in the repository (ADR 0001); the step runs only when they changed.
        var inputHash = BuildSources.InputHash(ComponentKind.Mixin, kit.Directory!, kit.Arguments);
        var component = await make(
            name,
            inputHash,
            async () =>
            {
                var directory = await kitBuildStep.BuildAsync(
                    kit,
                    buildDirectory,
                    cancellationToken
                );
                return new ComponentSource(
                    name,
                    ComponentKind.Mixin,
                    directory,
                    Path.Combine(directory, Path.GetFileName(descriptor)),
                    kit.Arguments,
                    Inputs: kit.Directory
                );
            }
        );

        return new ManifestKit
        {
            Name = kit.Name,
            Reference = component.Directory,
            IsLocal = true,
            Component = component.Name,
            Arguments = kit.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }

    private async Task<ManifestKit> RegistryKitAsync(
        KitSource kit,
        CancellationToken cancellationToken
    )
    {
        var pinned = await PinAsync(kit.Reference, cancellationToken);
        return new ManifestKit
        {
            Name = kit.Name,
            Reference = kit.Reference,
            IsLocal = false,
            Digest = pinned.Digest,
            Arguments = kit.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }

    private async Task ConfigurationAsync(
        string team,
        ResolvedSquad squad,
        BuildManifest manifest,
        string buildDirectory,
        Make make
    )
    {
        var directory = Path.Combine(buildDirectory, SquadConfigurationMixin.MixinName);
        SquadConfigurationMixin.Generate(team, manifest, squad.Secrets, directory);
        var source = MixinSource(SquadConfigurationMixin.MixinName, directory);
        await make(
            ComponentNames.Configuration,
            source.InputHash,
            () =>
            {
                // squad.json records when its content last changed.
                manifest.CreatedAt = DateTimeOffset.Now;
                SquadConfigurationMixin.Generate(team, manifest, squad.Secrets, directory);
                return Task.FromResult(MixinSource(SquadConfigurationMixin.MixinName, directory));
            }
        );
        manifest.ConfigurationMixinDirectory = directory;
    }

    private static ComponentSource MixinSource(string name, string directory) =>
        new(
            name,
            ComponentKind.Mixin,
            directory,
            Path.Combine(directory, name + ".yaml"),
            new Dictionary<string, string>()
        );

    private BuildManifest? Previous(SquadBuildIdentity identity)
    {
        try
        {
            return store.Load(identity);
        }
        catch (AboxException)
        {
            // An unreadable or foreign record is replaced by a full build.
            return null;
        }
    }

    private static string Repository(ResolvedSquad squad, SquadBuildIdentity identity)
    {
        if (!WorkloadFile.IsValidPublishRepository(squad.Workload.PublishRepository))
        {
            throw new AboxException(
                $"The workload '{squad.Workload.Name}' has no valid publishRepository, so its component images have no registry name. Set 'publishRepository' in '{squad.Workload.Path}'."
            );
        }

        return $"{RegistryImageName.Registry}/{squad.Workload.PublishRepository}/{identity.Repository}";
    }

    private async Task<PinnedImage> PinAsync(string reference, CancellationToken cancellationToken)
    {
        var at = reference.LastIndexOf('@');
        var digest =
            at >= 0
                ? reference[(at + 1)..]
                : await registry.DigestAsync(reference, cancellationToken);
        return new PinnedImage { Reference = reference, Digest = digest };
    }

    private static string PinnedReference(PinnedImage image) =>
        ImageReferences.Repository(image.Reference) + "@" + image.Digest;

    private static bool SameRegistryKits(
        IReadOnlyList<ManifestKit> previous,
        IReadOnlyList<ManifestKit> current
    ) =>
        previous
            .Where(kit => !kit.IsLocal)
            .Select(kit => (kit.Reference, kit.Digest))
            .SequenceEqual(
                current.Where(kit => !kit.IsLocal).Select(kit => (kit.Reference, kit.Digest))
            );

    private string Descriptor(string directory) =>
        KitDescriptor.Load(directory, parser)?.DescriptorPath
        ?? throw new AboxException($"The kit at '{directory}' has no descriptor.");

    /// <summary>
    /// A component image is built without kit arguments, so a squad may only give a local kit
    /// the arguments it reads when the sandbox is created.
    /// </summary>
    private void EnsureCreatePhaseArguments(KitSource kit, string descriptor)
    {
        var declarations = parser.ReadFile(descriptor).GetEntry("args");
        foreach (var name in kit.Arguments.Keys)
        {
            var declaration = declarations?.GetEntry(name);
            if (
                declaration is null
                || declaration.GetString("buildArg") is not null
                || declaration.GetString("buildSecret") is not null
            )
            {
                throw new AboxException(
                    $"The argument '{name}' of kit '{kit.Name}' is undeclared or build-phase. Only create-phase arguments can be given to a kit."
                );
            }
        }
    }

    private static BuildManifest Manifest(
        string teamName,
        ResolvedSquad squad,
        string tag,
        IReadOnlyList<ManifestKit> kits,
        PinnedImage baseImage,
        string imageStore
    )
    {
        var manifest = new BuildManifest
        {
            Squad = squad.Name,
            Tag = tag,
            Reference = ImageName.OfTeam(teamName, tag),
            RootDirectory = squad.RootDirectory,
            WorkloadName = squad.Workload.Name,
            PublishRepository = squad.Workload.PublishRepository,
            Template = squad.Workload.Template,
            Base = baseImage,
            ImageStore = imageStore,
            ConfigFiles = [.. squad.ConfigFiles, squad.Workload.Path],
            Kits = [.. kits],
        };

        foreach (var server in squad.Mcp)
        {
            manifest.Mcp.Add(new ManifestMcp { Name = server.Name, Url = server.Url });
        }

        foreach (var state in squad.State)
        {
            manifest.State.Add(
                new ManifestState
                {
                    SandboxPath = state.SandboxPath,
                    RelativePath = state.RelativePath,
                }
            );
        }

        return manifest;
    }
}
