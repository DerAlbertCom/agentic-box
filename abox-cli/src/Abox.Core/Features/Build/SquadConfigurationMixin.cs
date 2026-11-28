using System.Text.Json;
using System.Text.Json.Serialization;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Build;

/// <summary>
/// Writes the squad configuration mixin of a build: <c>/usr/share/abox/squad.json</c> and
/// its declaration-only descriptor. Local creation and publication use the same mixin.
/// </summary>
internal static class SquadConfigurationMixin
{
    /// <summary>The name of the generated mixin, without the extension.</summary>
    public const string MixinName = "config";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Writes the mixin into a directory and returns the directory. The credentials the
    /// kits ask for go into <c>squad.json</c>, so <c>create</c> can check them from a build
    /// or a publication without the team's sources.
    /// </summary>
    public static string Generate(
        string team,
        BuildManifest build,
        IReadOnlyList<SecretDeclaration> secrets,
        string targetDirectory,
        IReadOnlyList<CredentialRequest>? credentials = null
    )
    {
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(
            Path.Combine(targetDirectory, "squad.json"),
            JsonSerializer.Serialize(Metadata(team, build, secrets, credentials ?? []), JsonOptions)
        );
        File.WriteAllText(
            Path.Combine(targetDirectory, MixinName + ".yaml"),
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: mixin\ndisplayName: abox squad configuration\n"
        );
        File.WriteAllText(
            Path.Combine(targetDirectory, MixinName + ".dockerfile"),
            "FROM scratch\nCOPY squad.json /usr/share/abox/squad.json\n"
        );
        return targetDirectory;
    }

    private static SquadMetadata Metadata(
        string team,
        BuildManifest build,
        IReadOnlyList<SecretDeclaration> secrets,
        IReadOnlyList<CredentialRequest> credentials
    ) =>
        new()
        {
            Team = team,
            Squad = build.Squad,
            Tag = build.Tag,
            CreatedAt = build.CreatedAt,
            Mcp = build.Mcp.Select(mcp => new SquadMetadataMcp(mcp.Name, mcp.Url)).ToList(),
            State = build
                .State.Select(state => new SquadMetadataState(
                    state.SandboxPath,
                    state.RelativePath
                ))
                .ToList(),
            Kits = build.Kits.Select(kit => new SquadMetadataKit(kit.Name, kit.Arguments)).ToList(),
            Secrets = secrets.Select(SquadMetadataSecret.From).ToList(),
            Credentials = credentials
                .Select(request => new SquadMetadataCredential(
                    request.Service,
                    request.Kits,
                    request.Domains,
                    request.Optional,
                    request.Description
                ))
                .ToList(),
        };

    /// <summary>The public squad.json allowlist, deliberately independent of local build provenance.</summary>
    private sealed record SquadMetadata
    {
        public int SchemaVersion => 1;
        public required string Team { get; init; }
        public required string Squad { get; init; }
        public required string Tag { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
        public required IReadOnlyList<SquadMetadataMcp> Mcp { get; init; }
        public required IReadOnlyList<SquadMetadataState> State { get; init; }
        public required IReadOnlyList<SquadMetadataKit> Kits { get; init; }
        public required IReadOnlyList<SquadMetadataSecret> Secrets { get; init; }
        public required IReadOnlyList<SquadMetadataCredential> Credentials { get; init; }
    }

    private sealed record SquadMetadataMcp(string Name, string? Url);

    private sealed record SquadMetadataState(string SandboxPath, string RelativePath);

    private sealed record SquadMetadataKit(
        string Name,
        IReadOnlyDictionary<string, string> Arguments
    );
}
