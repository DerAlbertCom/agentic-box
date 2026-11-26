using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Sandboxes;

/// <summary>Resolves recovery provenance without writing diagnostics while rendering a command.</summary>
internal sealed class CreateRecoveryHints(
    AboxConfig config,
    BuildStore store,
    SandboxRecordStore records
)
{
    public CreateRecoveryHint Resolve(string name, string location, FolderConfig? folderConfig)
    {
        var record = records.Load(name);
        var squad =
            folderConfig?.Squad ?? (string.IsNullOrWhiteSpace(record?.Squad) ? null : record.Squad);
        var tag = string.IsNullOrWhiteSpace(record?.Tag) ? config.DefaultTag : record.Tag;
        var team = string.IsNullOrWhiteSpace(record?.Team) ? null : record.Team;
        var build = LoadBuild(record, squad, tag);
        ImageName.TryParseTeam(build.Manifest?.Reference, tag, out var buildTeam);
        team ??= buildTeam;
        team ??= config.DefaultTeam;

        var local =
            buildTeam is not null && buildTeam.Equals(team, StringComparison.OrdinalIgnoreCase);
        return new CreateRecoveryHint(
            Render(team, squad, tag, local ? build.Manifest : null, location),
            build.Diagnostic
        );
    }

    private LocalHintBuild LoadBuild(SandboxRecord? record, string? squad, string tag)
    {
        if (record is not { KitReference: null } || squad is null)
        {
            return new(null, null);
        }

        try
        {
            var manifest = string.IsNullOrWhiteSpace(record.Team)
                ? store.LoadLegacy(squad, tag)
                : store.Load(new SquadBuildIdentity(record.Team, squad, tag));
            return new(manifest, null);
        }
        catch (AboxException exception)
        {
            // Failed provenance excludes local flags, but still permits a remote recovery hint.
            return new(null, exception.Message);
        }
    }

    internal static string Render(
        string? team,
        string? squad,
        string tag,
        BuildManifest? localBuild,
        string location
    )
    {
        var placeholders =
            team is null || squad is null ? " (replace <name> placeholders)" : string.Empty;
        var local = localBuild is not null ? " --local" : string.Empty;
        return $"abox create --team {team ?? "<name>"} --squad {squad ?? "<name>"} --tag {tag}{local} --location \"{location}\"{placeholders}";
    }

    private sealed record LocalHintBuild(BuildManifest? Manifest, string? Diagnostic);
}

/// <summary>A rendered recovery instruction and any safe, nonfatal build-provenance diagnostic.</summary>
internal sealed record CreateRecoveryHint(string Command, string? Diagnostic)
{
    /// <summary>The diagnostic, if any, then the command after the prefix.</summary>
    public IEnumerable<string> Lines(string prefix) =>
        Diagnostic is null ? [prefix + Command] : [Diagnostic, prefix + Command];
}
