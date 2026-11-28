using Abox.Features.Shared.Common;
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
    /// current folder) with its folder configuration, loaded as <paramref name="loading"/>
    /// says.
    /// </summary>
    public async Task<SandboxLocation> SandboxLocationAsync(
        string? location,
        FolderConfigurationLoading loading,
        CancellationToken cancellationToken
    )
    {
        var fullPath = FullPath(location);
        var sandboxLocation = new SandboxLocation(
            fullPath,
            loading == FolderConfigurationLoading.Never
                ? _ => Task.FromResult<FolderConfig?>(null)
                : token => FolderConfig.LoadAsync(fullPath, parser, token)
        );
        if (loading == FolderConfigurationLoading.Now)
            await sandboxLocation.FolderConfigurationAsync(cancellationToken);
        return sandboxLocation;
    }

    /// <summary>The given team, otherwise the required <c>defaultTeam</c>.</summary>
    public string Team(string? team) =>
        team
        ?? configuration.Value.DefaultTeam
        ?? throw new AboxException(
            "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
        );

    /// <summary>The given team, otherwise <c>defaultTeam</c>; null when neither names one.</summary>
    public string? TeamOrNone(string? team) => team ?? configuration.Value.DefaultTeam;

    /// <summary>Every given team, otherwise the required <c>defaultTeam</c>.</summary>
    public IReadOnlyList<string> Teams(IReadOnlyList<string>? teams) =>
        teams is { Count: > 0 } ? teams : [Team(null)];

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

    private string FullPath(string? location) =>
        Path.GetFullPath(
            location ?? configuration.Value.DefaultLocation ?? Directory.GetCurrentDirectory()
        );
}
