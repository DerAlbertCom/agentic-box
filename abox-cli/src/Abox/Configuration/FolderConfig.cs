using System.Text;
using Abox.Infrastructure;

namespace Abox.Configuration;

/// <summary>Runtime-only opt-in in the sandbox folder, never a build input.</summary>
public sealed class FolderConfig
{
    private FolderConfig(
        string location,
        string? squad,
        IReadOnlyList<UserSecretSelection> userSecrets,
        int? portOffset,
        bool? terminalTheme
    )
    {
        Location = Path.GetFullPath(location);
        Squad = squad;
        UserSecrets = userSecrets;
        PortOffset = portOffset;
        TerminalTheme = terminalTheme;
    }

    public string? Squad { get; }
    public IReadOnlyList<UserSecretSelection> UserSecrets { get; }
    public int? PortOffset { get; }
    public bool? TerminalTheme { get; }
    internal string Location { get; }

    /// <summary>Select from the entry-time snapshot; never reread or write folder configuration.</summary>
    public static int SelectPortOffset(FolderConfig? folderConfig, int? explicitOffset = null)
    {
        var offset = explicitOffset ?? folderConfig?.PortOffset ?? 10000;
        ValidatePortOffset(offset, null);
        return offset;
    }

    /// <summary>Whether the sandbox recolors the terminal: the switch, then the folder, then the user default.</summary>
    public static bool SelectTerminalTheme(
        FolderConfig? folderConfig,
        bool userDefault,
        bool? explicitTerminalTheme = null
    ) => explicitTerminalTheme ?? folderConfig?.TerminalTheme ?? userDefault;

    /// <summary>The folder selects the squad; an explicit matching selection is allowed.</summary>
    public string? SelectSquad(string? explicitSquad)
    {
        if (
            Squad is not null
            && explicitSquad is not null
            && !Squad.Equals(explicitSquad, StringComparison.OrdinalIgnoreCase)
        )
            throw new AboxException(
                $"The selected squad conflicts with 'squad' in {FileNames}. Use the folder's squad or update the folder configuration before creating or replacing its sandbox."
            );
        return Squad ?? explicitSquad;
    }

    /// <summary>Do not implicitly replace a sandbox whose recorded squad differs or is unknown.</summary>
    public void EnsureSandboxSquad(string? recordedSquad)
    {
        if (Squad is not null && string.IsNullOrWhiteSpace(recordedSquad))
            throw new AboxException(
                $"Cannot verify the sandbox's squad against the folder configuration ({FileNames}). Create it with abox create for this folder before replacing it."
            );
        SelectSquad(recordedSquad);
    }

    /// <summary>
    /// Reads abox.yaml and lays abox.user.yaml over it (<see cref="YamlReader.Merge"/>), then
    /// validates the merged result. Missing configuration is a no-op for lifecycle callers.
    /// </summary>
    public static async Task<FolderConfig?> LoadAsync(
        string location,
        CancellationToken cancellationToken
    )
    {
        RejectJson(location, "abox.json", SharedFile);
        RejectJson(location, "abox.user.json", UserFile);
        var shared = await ReadAsync(Path.Combine(location, SharedFile), cancellationToken);
        var user = await ReadAsync(Path.Combine(location, UserFile), cancellationToken);
        var merged =
            shared is null ? user
            : user is null ? shared
            : YamlReader.Merge(shared, user);
        return merged is null ? null : Decode(location, merged);
    }

    public const string SharedFile = "abox.yaml";
    public const string UserFile = "abox.user.yaml";

    /// <summary>Both folder configuration files, for messages.</summary>
    public const string FileNames = $"{SharedFile} or {UserFile}";

    private static void RejectJson(string location, string jsonFile, string yamlFile)
    {
        var path = Path.Combine(location, jsonFile);
        if (File.Exists(path))
            throw new AboxException(
                $"'{path}' is no longer read. Rename it to {yamlFile}; the folder configuration is now YAML."
            );
    }

    private static async Task<YamlReader?> ReadAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        string yaml;
        try
        {
            yaml = await File.ReadAllTextAsync(
                path,
                new UTF8Encoding(false, true),
                cancellationToken
            );
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            throw new AboxException($"'{path}' has invalid UTF-8. Fix the folder configuration.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"Cannot read '{path}'. Check the folder and file permissions."
            );
        }
        return YamlReader.Parse(yaml, path);
    }

    private static FolderConfig Decode(string location, YamlReader reader)
    {
        reader.RejectUnknownKeys("squad", "userSecrets", "portOffset", "terminalTheme");
        var portOffset = reader.GetInteger("portOffset");
        if (portOffset is { } offset)
            ValidatePortOffset(offset, reader.SourceOf("portOffset"));
        return new FolderConfig(
            location,
            ReadSquad(reader),
            UserSecretSelection.Parse(reader),
            portOffset,
            reader.GetBoolean("terminalTheme")
        );
    }

    private static string? ReadSquad(YamlReader reader)
    {
        if (!reader.Has("squad"))
            return null;
        var squad = reader.GetString("squad");
        if (reader.IsNull("squad") || string.IsNullOrWhiteSpace(squad))
            throw new AboxException(
                $"'{reader.SourceOf("squad")}': 'squad' must be a nonempty string."
            );
        return squad;
    }

    private static void ValidatePortOffset(int offset, string? source)
    {
        if (offset is >= -65534 and <= 65534)
            return;
        var file = source is null ? string.Empty : $"'{source}': ";
        throw new AboxException(
            $"{file}Port offset must be an integer in -65534..65534; every resulting host port must also be in 1..65535."
        );
    }
}
