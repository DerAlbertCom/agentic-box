using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Configuration;

/// <summary>
/// Reads the user configuration file.
/// </summary>
internal static class AboxConfigLoader
{
    private static readonly string[] KnownKeys =
    [
        "defaultTeam",
        "defaultLocation",
        "teamsDirectory",
        "kitsDirectory",
        "defaultTag",
        "pullRepository",
        "userSecretsDenyList",
        "terminalTheme",
        "sourceRoot",
        "docsBaseUrl",
    ];

    public static AboxConfig Load(AboxPaths paths, IYamlParser parser)
    {
        if (!File.Exists(paths.ConfigFile))
        {
            return new AboxConfig();
        }

        var reader = parser.ReadFile(paths.ConfigFile);
        reader.RejectUnknownKeys(KnownKeys);

        var defaults = new AboxConfig();
        var pullRepository = Or(reader.GetString("pullRepository"), defaults.PullRepository);
        if (!RegistryImageName.IsValidRepository(pullRepository))
        {
            throw new AboxException(
                $"'{paths.ConfigFile}': 'pullRepository' is not a valid repository name. Use one lowercase registry path segment, without a host, slash or tag."
            );
        }

        return new AboxConfig
        {
            DefaultTeam = reader.GetString("defaultTeam"),
            DefaultLocation = reader.GetString("defaultLocation"),
            SourceRoot = reader.GetString("sourceRoot"),
            TeamsDirectory = Or(reader.GetString("teamsDirectory"), defaults.TeamsDirectory),
            KitsDirectory = Or(reader.GetString("kitsDirectory"), defaults.KitsDirectory),
            DefaultTag = reader.Has("defaultTag")
                ? reader.GetString("defaultTag") ?? string.Empty
                : defaults.DefaultTag,
            PullRepository = pullRepository,
            UserSecretsDenyList = reader.GetStringList("userSecretsDenyList"),
            TerminalTheme = reader.GetBoolean("terminalTheme") ?? defaults.TerminalTheme,
            DocsBaseUrl = Or(reader.GetString("docsBaseUrl"), defaults.DocsBaseUrl),
        };
    }

    /// <summary>
    /// Adds <c>sourceRoot</c> to the user configuration file, creating the file when it does
    /// not exist and keeping every other setting as written.
    /// </summary>
    public static void SaveSourceRoot(AboxPaths paths, string folder)
    {
        Directory.CreateDirectory(paths.ConfigDirectory);
        var existing = File.Exists(paths.ConfigFile) ? File.ReadAllText(paths.ConfigFile) : "";
        var separator = existing.Length == 0 || existing.EndsWith('\n') ? "" : "\n";
        File.WriteAllText(
            paths.ConfigFile,
            $"{existing}{separator}sourceRoot: '{folder.Replace("'", "''")}'\n"
        );
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
