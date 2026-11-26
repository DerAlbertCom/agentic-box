using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Publish;

/// <summary>All teams to validate before publishing, and the optional Buildx builder.</summary>
internal sealed record PublishRequest(
    IReadOnlyList<SquadResolver.ResolvedTeam> Teams,
    string Tag,
    string? Builder = null
);

/// <summary>
/// Publishes what <c>abox build</c> built: refuses stale builds, pushes the component images
/// unchanged, then builds and pushes only the set that composes them, and promotes it once
/// it is verified. The set is the one image publish builds, because the kit frontend
/// resolves set components only from the registry.
/// </summary>
internal sealed class PublishService(
    BuildStore store,
    IRegistryPublisher registry,
    IRegistryDigests digests,
    ILocalImageStore images,
    IConsole console
)
{
    public const string Registry = RegistryImageName.Registry;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<int> PublishAsync(PublishRequest request, CancellationToken cancellationToken)
    {
        RegistryImageName.ValidateTag(request.Tag);
        var builds = new PublicationSources(store).LoadBuilds(request);
        // Every selected squad is checked before Docker is used and before the first push.
        foreach (var selected in builds)
            PublicationFreshness.EnsureSourcesUnchanged(selected);
        if (!registry.IsInstalled)
            throw new AboxException(
                "Publishing v3 kits needs Docker with the Buildx plugin on PATH. Install it and configure a BuildKit builder; see docs/implementation.md."
            );

        var publisher = new VerifiedPublicationRegistry(registry, console, request.Builder);
        await publisher.RequireBuildxAsync(cancellationToken);
        var freshness = new PublicationFreshness(digests, images);
        foreach (var selected in builds)
            await freshness.EnsureImagesUnchangedAsync(selected, cancellationToken);
        await publisher.ValidateRegistryKitsAsync(
            builds.SelectMany(item => item.Build.Kits),
            cancellationToken
        );

        using var temporary = new TemporaryDirectory("abox-publish-", "publication", console);
        try
        {
            foreach (var selected in builds)
            {
                await PublishSquadAsync(
                    selected,
                    publisher,
                    Path.Combine(temporary.Path, Guid.NewGuid().ToString("N")),
                    request.Tag,
                    cancellationToken
                );
            }
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"Cannot prepare or read publication files: {exception.Message}",
                exception
            );
        }
    }

    private async Task PublishSquadAsync(
        SelectedPublicationBuild selected,
        VerifiedPublicationRegistry publisher,
        string directory,
        string tag,
        CancellationToken cancellationToken
    )
    {
        var build = selected.Build;
        var pinned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in build.Components)
        {
            pinned[component.Name] = await PushComponentAsync(
                component,
                build.ImageStore!,
                publisher,
                cancellationToken
            );
        }

        var repository = $"{Registry}/{build.PublishRepository}/{selected.Identity.Repository}";
        var candidate = await publisher.PushVerifiedAsync(
            new PublicationPush(
                directory,
                WriteSet(selected, pinned, directory),
                $"{repository}:publish-{Guid.NewGuid():N}",
                PublicationImageKind.Set,
                directory
            ),
            cancellationToken
        );
        var reference = $"{repository}:{tag}";
        // The requested (possibly moving) tag is untouched until the candidate passes inspection.
        await publisher.PromoteAndConfirmAsync(candidate, reference, cancellationToken);
        console.WriteLine($"Published squad '{build.Squad}' as '{reference}'.");
    }

    /// <summary>Pushes a component image unchanged and returns it pinned by its build digest.</summary>
    private async Task<string> PushComponentAsync(
        BuildComponent component,
        string imageStore,
        VerifiedPublicationRegistry publisher,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine($"Pushing '{component.Reference}'...");
        await registry.PushImageAsync(imageStore, component.Reference, cancellationToken);
        var pushed = await digests.DigestAsync(component.Reference, cancellationToken);
        if (pushed != component.Digest)
        {
            throw new AboxException(
                $"The registry holds '{component.Reference}' as {pushed}, not as the built {component.Digest}. The requested set tag was not promoted; run 'abox build --rebuild' and publish again."
            );
        }

        var image = ImageReferences.Repository(component.Reference) + "@" + component.Digest;
        await publisher.VerifyAsync(
            image,
            component.Kind == ComponentKind.Workload
                ? PublicationImageKind.Workload
                : PublicationImageKind.Mixin,
            cancellationToken
        );
        return image;
    }

    /// <summary>
    /// Writes the set descriptor: the workload, the kits as declared (local ones as their
    /// pushed images, registry ones as pinned at build time, each with its create-phase
    /// arguments), the state and the configuration. Returns the descriptor's path.
    /// </summary>
    private static string WriteSet(
        SelectedPublicationBuild selected,
        IReadOnlyDictionary<string, string> pinned,
        string directory
    )
    {
        var build = selected.Build;
        var kits = new List<PublicationComponent> { new(pinned[ComponentNames.Workload]) };
        foreach (var kit in build.Kits)
        {
            var source =
                kit.IsLocal ? pinned[kit.Component!]
                : kit.Digest is { } digest
                    ? ImageReferences.Repository(kit.Reference) + "@" + digest
                : kit.Reference;
            kits.Add(new PublicationComponent(source, kit.Arguments));
        }
        if (pinned.TryGetValue(ComponentNames.State, out var state))
            kits.Add(new PublicationComponent(state));
        kits.Add(new PublicationComponent(pinned[ComponentNames.Configuration]));

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "set.yaml");
        File.WriteAllText(
            path,
            "# syntax=docker/sandbox-kit:3\n"
                + JsonSerializer.Serialize(
                    new PublicationSetDescriptor
                    {
                        DisplayName = $"{selected.Identity.Team} / {build.Squad}",
                        Kits = kits,
                    },
                    JsonOptions
                )
        );
        return path;
    }
}
