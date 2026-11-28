using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Secrets;

/// <summary>
/// Reads the secrets of a squad without a sandbox and without team sources: from the
/// <c>squad.json</c> of a local build, or from the bundled metadata of a published set
/// (the set's inspection and its small configuration mixin, no workload or kit layers).
/// </summary>
internal sealed class SquadSecretsReader(
    BuildStore builds,
    IPublishedSquadSource published,
    AboxConfig config,
    IConsole console
)
{
    /// <summary>The secrets the bundled metadata of the published set records.</summary>
    public async Task<SquadSecrets> ReadPublishedAsync(
        SquadBuildIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var reference = RegistryImageName.OfSquad(config.PullRepository, identity);
        console.WriteLine($"Reading the published squad set '{reference}'...");
        var squad = await published.LoadAsync(reference, identity, cancellationToken);
        return new SquadSecrets(squad.Metadata.Credentials, squad.Metadata.Secrets);
    }

    /// <summary>The secrets the <c>squad.json</c> of the local build records.</summary>
    public SquadSecrets ReadLocal(SquadBuildIdentity identity)
    {
        var rebuild = $"abox build --team {identity.Team} --tag {identity.Tag}";
        var record =
            builds.Load(identity)
            ?? throw new AboxException(
                $"There is no local build of '{identity.Squad}' with the tag '{identity.Tag}'. Prepare it first: {rebuild}"
            );
        var json = record.ConfigurationMixinDirectory is { } directory
            ? Path.Combine(directory, "squad.json")
            : null;
        if (json is null || !File.Exists(json))
        {
            throw new AboxException(
                $"The local build of '{identity.Squad}:{identity.Tag}' has no squad metadata (an older build). Run '{rebuild}' again."
            );
        }

        return SquadSecrets.ReadSquadJson(
            File.ReadAllText(json),
            $"The local build of '{identity.Squad}:{identity.Tag}'"
        );
    }
}
