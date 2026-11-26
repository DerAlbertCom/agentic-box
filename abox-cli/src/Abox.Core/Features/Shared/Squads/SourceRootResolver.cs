using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Squads;

/// <summary>
/// Finds the directory <c>abox build</c> and <c>abox publish</c> search the team sources
/// from: <c>--root</c>, the current directory when the team sources are found from there,
/// the <c>sourceRoot</c> setting, then a prompt that saves the answer as <c>sourceRoot</c>.
/// </summary>
internal sealed class SourceRootResolver(
    AboxConfig config,
    AboxPaths paths,
    IConsole console,
    IYamlParser parser
)
{
    private readonly SquadSourceDiscovery _discovery = new(config, parser);

    /// <summary>
    /// The start directory for the <paramref name="teams"/>. A given <paramref name="root"/>
    /// is returned as given.
    /// </summary>
    public string Resolve(string? root, string currentDirectory, IReadOnlyList<string> teams)
    {
        if (root is not null)
            return root;
        if (HasTeamSources(currentDirectory, teams))
        {
            OfferToRemember(currentDirectory, teams);
            return currentDirectory;
        }

        var teamNames = string.Join(", ", teams.Select(team => $"'{team}'"));
        if (config.SourceRoot is { } configured)
        {
            return HasTeamSources(configured, teams)
                ? configured
                : throw new AboxException(
                    $"The 'sourceRoot' of the user configuration, '{configured}', has no team sources for {teamNames}. Fix 'sourceRoot' or give --root <directory>."
                );
        }

        var missing =
            $"No team sources for {teamNames} are found from the current directory. Give --root <directory> or set 'sourceRoot' in the user configuration.";
        if (!console.IsInteractive)
            throw new AboxException(missing);

        var answer = console.ReadLine(
            $"No team sources for {teamNames} are found from the current directory. Which repository folder holds the team sources? "
        );
        if (string.IsNullOrWhiteSpace(answer))
            throw new AboxException(missing);

        var folder = Path.GetFullPath(answer.Trim(), currentDirectory);
        if (!HasTeamSources(folder, teams))
        {
            throw new AboxException(
                $"'{folder}' has no team sources for {teamNames}. Give --root <directory> or set 'sourceRoot' in the user configuration."
            );
        }

        AboxConfigLoader.SaveSourceRoot(paths, folder);
        console.WriteLine($"Saved 'sourceRoot: {folder}' in '{paths.ConfigFile}'.");
        return folder;
    }

    /// <summary>
    /// A build or publish inside the repository is the first time abox sees where the team
    /// sources are: without a <c>sourceRoot</c>, an interactive run offers to remember the
    /// repository folder, so later runs outside it find the sources too.
    /// </summary>
    private void OfferToRemember(string currentDirectory, IReadOnlyList<string> teams)
    {
        if (config.SourceRoot is not null || !console.IsInteractive)
            return;

        var repository = _discovery.TeamDirectory(currentDirectory, teams[0]).RootDirectory;
        if (
            console.Confirm(
                $"Remember '{repository}' as 'sourceRoot', so build and publish find the team sources from other folders too?"
            )
        )
        {
            AboxConfigLoader.SaveSourceRoot(paths, repository);
            console.WriteLine($"Saved 'sourceRoot: {repository}' in '{paths.ConfigFile}'.");
        }
    }

    private bool HasTeamSources(string directory, IReadOnlyList<string> teams) =>
        Directory.Exists(directory) && teams.All(team => _discovery.HasTeam(directory, team));
}
