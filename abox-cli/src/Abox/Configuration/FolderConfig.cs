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
        int? portOffset
    )
    {
        Location = Path.GetFullPath(location);
        Squad = squad;
        UserSecrets = userSecrets;
        PortOffset = portOffset;
    }

    public string? Squad { get; }
    public IReadOnlyList<UserSecretSelection> UserSecrets { get; }
    public int? PortOffset { get; }
    internal string Location { get; }

    /// <summary>Select from the entry-time snapshot; never reread or write folder configuration.</summary>
    public static int SelectPortOffset(FolderConfig? folderConfig, int? explicitOffset = null)
    {
        var offset = explicitOffset ?? folderConfig?.PortOffset ?? 10000;
        if (offset is < -65534 or > 65534)
            throw new AboxException(
                "Port offset must be an integer in -65534..65534; every resulting host port must also be in 1..65535."
            );
        return offset;
    }

    /// <summary>The folder selects the squad; an explicit matching selection is allowed.</summary>
    public string? SelectSquad(string? explicitSquad)
    {
        if (
            Squad is not null
            && explicitSquad is not null
            && !Squad.Equals(explicitSquad, StringComparison.OrdinalIgnoreCase)
        )
            throw new AboxException(
                "The selected squad conflicts with 'squad' in abox.yaml or abox.user.yaml. Use the folder's squad or update the folder configuration before creating or replacing its sandbox."
            );
        return Squad ?? explicitSquad;
    }

    /// <summary>Do not implicitly replace a sandbox whose recorded squad differs or is unknown.</summary>
    public void EnsureSandboxSquad(string? recordedSquad)
    {
        if (Squad is not null && string.IsNullOrWhiteSpace(recordedSquad))
            throw new AboxException(
                "Cannot verify the sandbox's squad against the folder configuration (abox.yaml, abox.user.yaml). Create it with abox create for this folder before replacing it."
            );
        SelectSquad(recordedSquad);
    }

    /// <summary>
    /// Reads abox.yaml and abox.user.yaml; the user file replaces every key it sets.
    /// Missing configuration is a no-op for lifecycle callers.
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
        if (shared is null && user is null)
            return null;
        // Every key is a value or a list, so the user file replaces it as a whole.
        return new FolderConfig(
            location,
            user?.Squad ?? shared?.Squad,
            user?.UserSecrets ?? shared?.UserSecrets ?? [],
            user?.PortOffset ?? shared?.PortOffset
        );
    }

    private const string SharedFile = "abox.yaml";
    private const string UserFile = "abox.user.yaml";

    private sealed record FolderFile(
        string? Squad,
        IReadOnlyList<UserSecretSelection>? UserSecrets,
        int? PortOffset
    );

    private static void RejectJson(string location, string json, string yaml)
    {
        var path = Path.Combine(location, json);
        if (File.Exists(path))
            throw new AboxException(
                $"'{path}' is no longer read. Rename it to {yaml}; the folder configuration is now YAML."
            );
    }

    private static async Task<FolderFile?> ReadAsync(
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
        return Decode(YamlReader.Parse(yaml, path));
    }

    private static FolderFile Decode(YamlReader reader)
    {
        reader.RejectUnknownKeys("squad", "userSecrets", "portOffset");
        return new FolderFile(
            ReadSquad(reader),
            reader.Has("userSecrets") ? UserSecretSelection.Parse(reader) : null,
            ReadPortOffset(reader)
        );
    }

    private static string? ReadSquad(YamlReader reader)
    {
        if (!reader.Has("squad"))
            return null;
        var squad = reader.GetString("squad");
        if (reader.IsNull("squad") || string.IsNullOrWhiteSpace(squad))
            throw new AboxException($"'{reader.Source}': 'squad' must be a nonempty string.");
        return squad;
    }

    private static int? ReadPortOffset(YamlReader reader)
    {
        var offset = reader.GetInteger("portOffset");
        if (offset is < -65534 or > 65534)
            throw new AboxException(
                $"'{reader.Source}': 'portOffset' must be an integer in -65534..65534."
            );
        return offset;
    }
}
