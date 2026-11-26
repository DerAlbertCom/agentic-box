using System.Text;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Configuration;

/// <summary>Runtime-only opt-in in the sandbox folder, never a build input.</summary>
internal sealed class FolderConfig
{
    private FolderConfig(
        string location,
        string? squad,
        IReadOnlyList<UserSecretSelection> userSecrets,
        int? portOffset,
        bool? terminalTheme,
        int? memory,
        int? cpus
    )
    {
        Location = Path.GetFullPath(location);
        Squad = squad;
        UserSecrets = userSecrets;
        PortOffset = portOffset;
        TerminalTheme = terminalTheme;
        Memory = memory;
        Cpus = cpus;
    }

    public string? Squad { get; }
    public IReadOnlyList<UserSecretSelection> UserSecrets { get; }
    public int? PortOffset { get; }
    public bool? TerminalTheme { get; }

    /// <summary>The memory of the sandbox in GB; null leaves the sbx default.</summary>
    public int? Memory { get; }

    /// <summary>The CPUs of the sandbox; null leaves the sbx default.</summary>
    public int? Cpus { get; }
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
        IYamlParser parser,
        CancellationToken cancellationToken
    )
    {
        RejectJson(location, "abox.json", SharedFile);
        RejectJson(location, "abox.user.json", UserFile);
        var shared = await ReadAsync(Path.Combine(location, SharedFile), parser, cancellationToken);
        var user = await ReadAsync(Path.Combine(location, UserFile), parser, cancellationToken);
        var merged =
            shared is null ? user
            : user is null ? shared
            : YamlReader.Merge(shared, user);
        return merged is null ? null : Decode(location, merged);
    }

    /// <summary>
    /// Saves the given memory (GB) and CPUs in the folder's abox.user.yaml, so later creates
    /// use them. Only the top-level <c>memory</c> and <c>cpus</c> lines are replaced or
    /// added; the rest of the file, comments included, stays as it is. Nothing is written
    /// unless the result is a valid folder configuration file.
    /// </summary>
    public static async Task SaveResourcesAsync(
        string location,
        int? memory,
        int? cpus,
        IYamlParser parser,
        CancellationToken cancellationToken
    )
    {
        if (memory is null && cpus is null)
            return;
        var path = Path.Combine(location, UserFile);
        try
        {
            var yaml = File.Exists(path)
                ? await File.ReadAllTextAsync(
                    path,
                    new UTF8Encoding(false, true),
                    cancellationToken
                )
                : string.Empty;
            if (memory is { } gigabytes)
                yaml = WithTopLevelValue(yaml, MemoryKey, gigabytes);
            if (cpus is { } count)
                yaml = WithTopLevelValue(yaml, CpusKey, count);
            var saved = Decode(location, parser.Read(yaml, path));
            if (
                (memory is not null && saved.Memory != memory)
                || (cpus is not null && saved.Cpus != cpus)
            )
                throw new AboxException(
                    $"Cannot save '{MemoryKey}' and '{CpusKey}' in '{path}'. Set them there by hand."
                );
            await File.WriteAllTextAsync(path, yaml, new UTF8Encoding(false), cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            throw new AboxException($"'{path}' has invalid UTF-8. Fix the folder configuration.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"Cannot write '{path}'. Check the folder and file permissions."
            );
        }
    }

    private static string WithTopLevelValue(string yaml, string key, int value)
    {
        var newLine = yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = yaml.Length == 0 ? [] : [.. yaml.Split(newLine)];
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        var entry = $"{key}: {value}";
        var index = lines.FindIndex(line =>
            line.StartsWith(key, StringComparison.Ordinal)
            && line[key.Length..].TrimStart(' ', '\t').StartsWith(':')
        );
        if (index >= 0)
            lines[index] = entry;
        else
            lines.Add(entry);
        return string.Join(newLine, lines) + newLine;
    }

    public const string MemoryKey = "memory";
    public const string CpusKey = "cpus";

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
        IYamlParser parser,
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
        return parser.Read(yaml, path);
    }

    private static FolderConfig Decode(string location, YamlReader reader)
    {
        reader.RejectUnknownKeys(
            "squad",
            "userSecrets",
            "portOffset",
            "terminalTheme",
            MemoryKey,
            CpusKey
        );
        var portOffset = reader.GetInteger("portOffset");
        if (portOffset is { } offset)
            ValidatePortOffset(offset, reader.SourceOf("portOffset"));
        return new FolderConfig(
            location,
            ReadSquad(reader),
            UserSecretSelection.Parse(reader),
            portOffset,
            reader.GetBoolean("terminalTheme"),
            ReadPositive(reader, MemoryKey),
            ReadPositive(reader, CpusKey)
        );
    }

    private static int? ReadPositive(YamlReader reader, string key)
    {
        var value = reader.GetInteger(key);
        if (value is < 1)
            throw new AboxException(
                $"'{reader.SourceOf(key)}': '{key}' must be a whole number of at least 1."
            );
        return value;
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
