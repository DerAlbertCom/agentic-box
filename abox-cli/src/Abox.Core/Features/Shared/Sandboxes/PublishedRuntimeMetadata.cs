using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Sandboxes;

/// <summary>
/// Decodes the authoritative runtime settings of a squad from its config mixin image, saved
/// as an archive: the one way published and local builds are read, including legacy schema
/// compatibility.
/// </summary>
internal static class PublishedRuntimeMetadata
{
    /// <summary>
    /// The runtime settings in the <c>squad.json</c> of a saved config mixin image.
    /// <paramref name="hostMounts"/> are the paths the squad's kits declare host mounts for;
    /// every state directory needs one.
    /// </summary>
    internal static BuildManifest ReadImage(
        string archive,
        SquadBuildIdentity expected,
        IReadOnlyCollection<string> hostMounts
    ) => Read(PublishedConfigArchive.Read(archive), expected, hostMounts);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = true,
    };

    internal static BuildManifest Read(
        string json,
        SquadBuildIdentity expected,
        IReadOnlyCollection<string> hostMounts
    )
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        ValidateIdentity(root, expected);
        var state =
            root.GetProperty("State").Deserialize<List<ManifestState>>(JsonOptions)
            ?? throw new JsonException("Missing State.");
        var mcp =
            root.GetProperty("Mcp").Deserialize<List<ManifestMcp>>(JsonOptions)
            ?? throw new JsonException("Missing Mcp.");
        var createdAt = root.GetProperty("CreatedAt").GetDateTimeOffset();
        if (
            createdAt == default
            || state.Any(item => item is null)
            || mcp.Any(item => item is null)
        )
            throw new JsonException("Incomplete runtime metadata.");
        ValidateState(state, hostMounts);
        ValidateMcp(mcp);
        return new BuildManifest
        {
            Squad = expected.Squad,
            Tag = expected.Tag,
            CreatedAt = createdAt,
            State = state,
            Mcp = mcp,
        };
    }

    private static void ValidateIdentity(JsonElement root, SquadBuildIdentity expected)
    {
        // The original publisher omitted SchemaVersion; its otherwise identical format is version 1.
        if (root.TryGetProperty("SchemaVersion", out var version) && version.GetInt32() != 1)
            throw new JsonException("Squad metadata schema mismatch.");
        if (
            !string.Equals(
                root.GetProperty("Team").GetString(),
                expected.Team,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                root.GetProperty("Squad").GetString(),
                expected.Squad,
                StringComparison.OrdinalIgnoreCase
            )
            || root.GetProperty("Tag").GetString() != expected.Tag
        )
            throw new JsonException("Squad metadata identity mismatch.");
    }

    private static void ValidateState(
        IReadOnlyList<ManifestState> state,
        IReadOnlyCollection<string> hostMounts
    )
    {
        try
        {
            StateEntry.EnsureDisjoint(
                state
                    .Select(entry => StateEntry.FromStored(entry.SandboxPath, entry.RelativePath))
                    .ToList()
            );
        }
        catch (AboxException exception)
        {
            throw new JsonException("Unsafe or overlapping state paths.", exception);
        }

        foreach (var entry in state)
        {
            if (!hostMounts.Contains(entry.SandboxPath))
                throw new JsonException("Unsafe state path or missing host-mount declaration.");
        }
    }

    /// <summary>The capability a kit declares a host mount with.</summary>
    internal const string HostMountCapability = "com.docker.sandbox/host-mount@1";

    private static void ValidateMcp(IReadOnlyList<ManifestMcp> mcp)
    {
        if (mcp.DistinctBy(item => item.Name).Count() != mcp.Count)
            throw new JsonException("Duplicate MCP declarations.");
        try
        {
            foreach (var server in mcp)
                new McpDeclaration(server.Name, server.Url).Validate("bundled squad metadata");
        }
        catch (AboxException exception)
        {
            throw new JsonException("Invalid MCP declaration.", exception);
        }
    }
}
