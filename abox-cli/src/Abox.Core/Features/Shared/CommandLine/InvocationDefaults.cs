using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// The one place where options get their defaults from the user configuration. It loads
/// the user configuration only when a default is first asked for, so only actions load it.
/// It also hands binding the arguments after the "--" of the invocation.
/// </summary>
internal sealed class InvocationDefaults(
    Lazy<AboxConfig> configuration,
    IYamlParser parser,
    TrailingArguments trailing
)
{
    /// <summary>The arguments after the "--".</summary>
    public IReadOnlyList<string> TrailingArguments => trailing.Values;

    /// <summary>
    /// The sandbox location (the given location, then <c>defaultLocation</c>, then the
    /// current folder) with its folder configuration, loaded now.
    /// </summary>
    public async Task<SandboxLocation> SandboxLocationAsync(
        string? location,
        CancellationToken cancellationToken
    )
    {
        var sandboxLocation = SandboxLocationWithDeferredFolderConfiguration(location);
        await sandboxLocation.FolderConfigurationAsync(cancellationToken);
        return sandboxLocation;
    }

    /// <summary>
    /// The sandbox location, whose folder configuration is loaded only when it is first
    /// asked for.
    /// </summary>
    public SandboxLocation SandboxLocationWithDeferredFolderConfiguration(string? location)
    {
        var fullPath = configuration.Value.ResolveLocation(location);
        return new SandboxLocation(
            fullPath,
            cancellationToken => FolderConfig.LoadAsync(fullPath, parser, cancellationToken)
        );
    }

    /// <summary>The sandbox location without reading its folder configuration.</summary>
    public SandboxLocation SandboxLocationWithoutFolderConfiguration(string? location) =>
        new(
            configuration.Value.ResolveLocation(location),
            _ => Task.FromResult<FolderConfig?>(null)
        );

    /// <summary>The given team, otherwise the required <c>defaultTeam</c>.</summary>
    public string Team(string? team) => configuration.Value.ResolveTeam(team);

    /// <summary>Every given team, otherwise the required <c>defaultTeam</c>.</summary>
    public IReadOnlyList<string> Teams(IReadOnlyList<string>? teams) =>
        configuration.Value.ResolveTeams(teams);

    /// <summary>The given tag, otherwise <c>defaultTag</c>, validated as a registry tag.</summary>
    public string Tag(string? tag)
    {
        var selected = TagWithoutValidation(tag);
        RegistryImageName.ValidateTag(selected);
        return selected;
    }

    /// <summary>
    /// The given tag, otherwise <c>defaultTag</c>, for a caller that validates it later.
    /// </summary>
    public string TagWithoutValidation(string? tag) => tag ?? configuration.Value.DefaultTag;
}
