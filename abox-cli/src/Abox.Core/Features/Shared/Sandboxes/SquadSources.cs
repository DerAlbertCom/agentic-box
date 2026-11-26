using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;

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
/// Uses only validated local build metadata and prebuilt workload images. Like a published
/// set, it needs no team sources: only <c>abox build</c> reads them.
/// </summary>
internal sealed class LocalSquadSource(BuildStore store, LocalWorkloadImages images) : ISquadSource
{
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
        var manifest =
            store.Load(identity)
            ?? throw new AboxException(
                $"There is no local build of '{identity.Squad}' with the tag '{identity.Tag}'. "
                    + $"Prepare it first: abox build --team {identity.Team} --tag {identity.Tag}"
            );
        var image =
            manifest.LocalWorkload
            ?? throw new AboxException(
                $"This build has no prebuilt local workload (legacy build). Run 'abox build --team {identity.Team} --tag {identity.Tag}' before using --local. Create will not build the workload from sources."
            );
        await images.EnsureAvailableAsync(image, cancellationToken);
        var arguments = KitArguments(manifest.Kits);
        var mixins = manifest.Kits.Select(kit => kit.Reference).ToList();
        if (manifest.StateMixinDirectory is not null)
            mixins.Add(manifest.StateMixinDirectory);
        if (manifest.ConfigurationMixinDirectory is not null)
            mixins.Add(manifest.ConfigurationMixinDirectory);
        return new PreparedSquad(
            manifest,
            image.RuntimeKitDirectory,
            mixins,
            arguments,
            PullPolicy: PullPolicy.Never,
            HostImageStore: image.ImageStore
        );
    }

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
