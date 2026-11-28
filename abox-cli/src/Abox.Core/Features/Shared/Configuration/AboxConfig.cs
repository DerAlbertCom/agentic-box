namespace Abox.Features.Shared.Configuration;

/// <summary>
/// The settings abox keeps for the user.
/// </summary>
internal sealed class AboxConfig
{
    /// <summary>The team used when the command line does not name one. Null means none.</summary>
    public string? DefaultTeam { get; init; }

    /// <summary>The folder used when the command line does not name one. Null means the current folder.</summary>
    public string? DefaultLocation { get; init; }

    /// <summary>
    /// The repository folder with the team sources, used by <c>abox build</c> and
    /// <c>abox publish</c> when no team sources are found from the current directory. Null
    /// means none.
    /// </summary>
    public string? SourceRoot { get; init; }

    /// <summary>The directory of a project that holds a folder per team.</summary>
    public string TeamsDirectory { get; init; } = "teams";

    /// <summary>The directory, relative to the project root, that holds the kits.</summary>
    public string KitsDirectory { get; init; } = "kits";

    /// <summary>The tag a local build is stored under when no tag is given.</summary>
    public string DefaultTag { get; init; } = "latest";

    /// <summary>The JFrog repository used to pull published squad kit sets.</summary>
    public string PullRepository { get; init; } = "mtiae-docker-release";

    /// <summary>Case-insensitive literal substrings forbidden in selected original user-secret values.</summary>
    public IReadOnlyList<string> UserSecretsDenyList { get; init; } = [];

    /// <summary>The built-in documentation address: the web view of this repository's main branch.</summary>
    public const string DefaultDocsBaseUrl =
        "https://github.com/DerAlbertCom/agentic-box/blob/main/";

    /// <summary>
    /// The address the repository-relative documentation paths of the secrets are joined
    /// with, so the pages can move without a rebuild of published sets.
    /// </summary>
    public string DocsBaseUrl { get; init; } = DefaultDocsBaseUrl;

    /// <summary>Whether <c>abox run</c> lets the sandbox recolor the terminal, unless the folder or the command line says otherwise.</summary>
    public bool TerminalTheme { get; init; } = true;
}
