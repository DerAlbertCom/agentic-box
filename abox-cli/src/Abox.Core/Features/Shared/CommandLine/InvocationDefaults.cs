using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// The one place where options get their defaults from the user configuration. It loads
/// the user configuration only when a default is first asked for, so only actions load it.
/// </summary>
internal sealed class InvocationDefaults(Lazy<AboxConfig> configuration, IYamlParser parser)
{
    /// <summary>
    /// The sandbox location (the given location, then <c>defaultLocation</c>, then the
    /// current folder) with its folder configuration.
    /// </summary>
    public async Task<SandboxLocation> SandboxLocationAsync(
        string? location,
        CancellationToken cancellationToken
    )
    {
        var fullPath = configuration.Value.ResolveLocation(location);
        var folderConfiguration = await FolderConfig.LoadAsync(fullPath, parser, cancellationToken);
        return new SandboxLocation(fullPath, folderConfiguration);
    }

    /// <summary>The sandbox location without reading its folder configuration.</summary>
    public SandboxLocation SandboxLocationWithoutFolderConfiguration(string? location) =>
        new(configuration.Value.ResolveLocation(location), null);

    /// <summary>The given team, otherwise the required <c>defaultTeam</c>.</summary>
    public string Team(string? team) => configuration.Value.ResolveTeam(team);

    /// <summary>Every given team, otherwise the required <c>defaultTeam</c>.</summary>
    public IReadOnlyList<string> Teams(IReadOnlyList<string>? teams) =>
        configuration.Value.ResolveTeams(teams);

    /// <summary>The given tag, otherwise <c>defaultTag</c>, validated as a registry tag.</summary>
    public string Tag(string? tag)
    {
        var selected = tag ?? configuration.Value.DefaultTag;
        RegistryImageName.ValidateTag(selected);
        return selected;
    }
}
