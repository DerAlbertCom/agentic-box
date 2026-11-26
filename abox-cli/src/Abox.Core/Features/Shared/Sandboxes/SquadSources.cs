using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Sandboxes;

/// <summary>A squad identity resolved before validating the sandbox location.</summary>
internal sealed record SquadSelection(SquadBuildIdentity Identity, string? Reference = null);

/// <summary>The source-specific inputs to the shared sandbox creation steps.</summary>
internal sealed record PreparedSquad(
    BuildManifest Metadata,
    string KitSource,
    IReadOnlyList<string> Mixins,
    IReadOnlyDictionary<string, string> Arguments,
    PullPolicy PullPolicy,
    string? HostImageStore = null,
    string? KitReference = null
);

/// <summary>Resolves a squad identity, then prepares its kits after location validation.</summary>
internal interface ISquadSource
{
    SquadSelection Resolve(SandboxCreationRequest request);
    Task<PreparedSquad> LoadAsync(SquadSelection selection, CancellationToken cancellationToken);
}

/// <summary>
/// Uses a local build: its build record and the component images <c>abox build</c> made in
/// the host image store. Like a published set, it needs no team sources and builds no recipe:
/// it hands sbx thin directory kits that only wrap the images, because sbx resolves OCI kit
/// references only from the registry.
/// </summary>
internal sealed class LocalSquadSource(
    BuildStore store,
    ILocalImageStore images,
    ILocalImageArchives archives,
    IYamlParser parser,
    IConsole console
) : ISquadSource
{
    /// <summary>The folder of a build that holds the thin kits handed to sbx.</summary>
    public const string ThinKitsFolderName = "create";

    public SquadSelection Resolve(SandboxCreationRequest request)
    {
        RegistryImageName.ValidateTag(request.Tag);

        var squad =
            request.Squad
            ?? throw new AboxException(
                "Local create requires --squad <name>; local squad discovery is not supported."
            );
        return new SquadSelection(new SquadBuildIdentity(request.Team.Name, squad, request.Tag));
    }

    public async Task<PreparedSquad> LoadAsync(
        SquadSelection selection,
        CancellationToken cancellationToken
    )
    {
        var identity = selection.Identity;
        var rebuild = $"abox build --team {identity.Team} --tag {identity.Tag}";
        var record =
            store.Load(identity)
            ?? throw new AboxException(
                $"There is no local build of '{identity.Squad}' with the tag '{identity.Tag}'. "
                    + $"Prepare it first: {rebuild}"
            );
        if (record.ImageStore is not { } imageStore || record.Components.Count == 0)
        {
            throw new AboxException(
                $"The local build of '{identity.Squad}:{identity.Tag}' has no component images (an older build). Run '{rebuild}' before using --local. Create will not build from sources."
            );
        }

        await EnsureImagesAsync(record, imageStore, rebuild, cancellationToken);
        var metadata = await ReadMetadataAsync(
            record,
            imageStore,
            identity,
            rebuild,
            cancellationToken
        );
        var kits = WriteThinKits(
            record,
            Path.Combine(store.DirectoryFor(identity), ThinKitsFolderName)
        );
        return new PreparedSquad(
            metadata,
            kits[Component(record, ComponentNames.Workload).Name],
            Mixins(record, kits),
            KitArguments(record.Kits),
            PullPolicy: PullPolicy.Never,
            HostImageStore: imageStore
        );
    }

    /// <summary>Fails before the sandbox is touched when an image is missing or was replaced.</summary>
    private async Task EnsureImagesAsync(
        BuildManifest record,
        string imageStore,
        string rebuild,
        CancellationToken cancellationToken
    )
    {
        if (!images.IsInstalled)
        {
            throw new AboxException(
                $"Local create needs Docker with Buildx on PATH for the component images. Install Docker and run '{rebuild}' again."
            );
        }

        await images.RequireHostBuilderAsync(imageStore, cancellationToken);
        foreach (var component in record.Components)
        {
            var imageId = await images.FindImageIdAsync(
                imageStore,
                component.Reference,
                cancellationToken
            );
            if (imageId != component.ImageId)
            {
                throw new AboxException(
                    $"The component image '{component.Reference}' is missing or was replaced. Run '{rebuild}' again; create will not use other images."
                );
            }
        }
    }

    private async Task<BuildManifest> ReadMetadataAsync(
        BuildManifest record,
        string imageStore,
        SquadBuildIdentity identity,
        string rebuild,
        CancellationToken cancellationToken
    )
    {
        var config = Component(record, ComponentNames.Configuration);
        using var temporary = new TemporaryDirectory("abox-local-", "metadata", console);
        var archive = Path.Combine(temporary.Path, "config.tar");
        await archives.SaveAsync(imageStore, config.Reference, archive, cancellationToken);
        try
        {
            var metadata = PublishedRuntimeMetadata.ReadImage(
                archive,
                identity,
                HostMounts(record)
            );
            // The record keeps the squad's canonical spelling, whatever the command line used.
            metadata.Squad = record.Squad;
            return metadata;
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or IOException
                        or InvalidDataException
                        or FormatException
            )
        {
            throw new AboxException(
                $"The configuration image '{config.Reference}' has missing or invalid squad metadata: {(exception.InnerException as AboxException)?.Message ?? exception.Message} Run '{rebuild} --rebuild'. The existing sandbox was not replaced.",
                exception
            );
        }
    }

    /// <summary>The host-mount paths the state mixin of the build declares.</summary>
    private HashSet<string> HostMounts(BuildManifest record)
    {
        var state = record.Component(ComponentNames.State);
        if (state is null)
            return [];
        return parser
            .Read(state.Descriptor, state.Reference)
            .GetEntries("capabilities")
            .Where(capability =>
                capability.GetString("type") == PublishedRuntimeMetadata.HostMountCapability
            )
            .Select(capability => capability.GetEntry("config")?.GetString("path") ?? "")
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes one thin kit per component: the workload as <c>FROM</c> its image, and each
    /// mixin as <c>COPY --from</c> its image, since sbx keeps only the layers a mixin recipe
    /// adds on top of its <c>FROM</c>. Returns the kit directory of each component.
    /// </summary>
    private static Dictionary<string, string> WriteThinKits(BuildManifest record, string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        var kits = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in record.Components)
        {
            var kit = Path.Combine(directory, component.Name.Replace('/', '-'));
            Directory.CreateDirectory(kit);
            File.WriteAllText(Path.Combine(kit, component.DescriptorFile), component.Descriptor);
            var image = component.Pinned;
            File.WriteAllText(
                Path.Combine(kit, Path.ChangeExtension(component.DescriptorFile, ".dockerfile")),
                component.Kind == ComponentKind.Workload
                    ? $"FROM {image}\n"
                    : $"FROM scratch\nCOPY --from={image} / /\n"
            );
            kits[component.Name] = kit;
        }

        return kits;
    }

    /// <summary>The mixins in set order: the kits as declared, then state, then configuration.</summary>
    private static List<string> Mixins(
        BuildManifest record,
        IReadOnlyDictionary<string, string> kits
    )
    {
        var mixins = record
            .Kits.Select(kit =>
                kit.IsLocal ? kits[kit.Component ?? throw Unusable(kit.Name)] : kit.PinnedReference
            )
            .ToList();
        if (kits.TryGetValue(ComponentNames.State, out var state))
            mixins.Add(state);
        mixins.Add(kits[ComponentNames.Configuration]);
        return mixins;
    }

    private static BuildComponent Component(BuildManifest record, string name) =>
        record.Component(name) ?? throw Unusable(name);

    private static AboxException Unusable(string name) =>
        new(
            $"The local build has no component image for '{name}'. Run 'abox build' again before using --local."
        );

    private static Dictionary<string, string> KitArguments(IReadOnlyList<ManifestKit> kits)
    {
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kit in kits)
        {
            foreach (var (key, value) in kit.Arguments)
            {
                if (arguments.TryGetValue(key, out var existing) && existing != value)
                    throw new AboxException(
                        $"The kit argument '{key}' has conflicting values in kits '{owners[key]}' and '{kit.Name}'."
                    );
                if (arguments.TryAdd(key, value))
                    owners.Add(key, kit.Name);
            }
        }
        return arguments;
    }
}

/// <summary>Uses a published squad set, with no local build metadata fallback.</summary>
internal sealed class RemoteSquadSource(
    AboxConfig config,
    IKitSourceTrust kitSources,
    IPublishedSquadSource published
) : ISquadSource
{
    public SquadSelection Resolve(SandboxCreationRequest request)
    {
        var squad =
            request.Squad
            ?? throw new AboxException(
                "Remote create requires --squad <name>; published squad discovery is not supported."
            );
        var identity = new SquadBuildIdentity(request.Team.Name, squad, request.Tag);
        var reference = RegistryImageName.OfSquad(config.PullRepository, identity);
        return new SquadSelection(identity, reference);
    }

    public async Task<PreparedSquad> LoadAsync(
        SquadSelection selection,
        CancellationToken cancellationToken
    )
    {
        await kitSources.EnsureTrustedAsync(
            RegistryImageName.PullRepositoryPrefix(config.PullRepository),
            cancellationToken
        );
        var remote = await published.LoadAsync(
            selection.Reference!,
            selection.Identity,
            cancellationToken
        );
        return new PreparedSquad(
            remote.Metadata,
            remote.Reference,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            PullPolicy: PullPolicy.Always,
            KitReference: remote.Reference
        );
    }
}
