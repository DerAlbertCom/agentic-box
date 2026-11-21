using System.Text.Json;
using System.Text.Json.Serialization;
using Abox.Kits;

namespace Abox.LocalState;

/// <summary>
/// Writes the squad configuration mixin of a build: <c>/usr/share/abox/squad.json</c> and
/// its declaration-only descriptor. Local creation and publication use the same mixin.
/// </summary>
public static class SquadConfigurationMixin
{
    /// <summary>The name of the generated mixin, without the extension.</summary>
    public const string MixinName = "config";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Writes the mixin into a directory and returns the directory.</summary>
    public static string Generate(
        string team,
        BuildManifest build,
        IReadOnlyList<SecretDeclaration> secrets,
        string targetDirectory
    )
    {
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(
            Path.Combine(targetDirectory, "squad.json"),
            JsonSerializer.Serialize(Metadata(team, build, secrets), JsonOptions)
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
        IReadOnlyList<SecretDeclaration> secrets
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
    }

    private sealed record SquadMetadataMcp(string Name, string? Url);

    private sealed record SquadMetadataState(string SandboxPath, string RelativePath);

    private sealed record SquadMetadataKit(
        string Name,
        IReadOnlyDictionary<string, string> Arguments
    );

    /// <summary>How to ask for a service's value; a declaration only, never a value or a host command; a command-backed secret is only marked.</summary>
    private sealed record SquadMetadataSecret(
        string Service,
        string? Prompt,
        IReadOnlyList<string> Hosts,
        string? Env,
        IReadOnlyList<SquadMetadataSecretInput> Inputs,
        string Separator,
        string Encoding,
        string? Placeholder,
        bool FromCommand
    )
    {
        internal static SquadMetadataSecret From(SecretDeclaration declaration) =>
            new(
                declaration.Service,
                declaration.Prompt,
                declaration.Hosts ?? [],
                declaration.EnvironmentVariable,
                (declaration.Inputs ?? [])
                    .Select(input => new SquadMetadataSecretInput(input.Name, input.Prompt))
                    .ToList(),
                declaration.Separator,
                declaration.Encoding.ToString().ToLowerInvariant(),
                declaration.Placeholder,
                declaration.Command is not null || declaration.CommandWithheld
            );
    }

    private sealed record SquadMetadataSecretInput(string Name, string? Prompt);
}
