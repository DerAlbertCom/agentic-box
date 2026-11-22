using System.Text;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.UserSecrets;

namespace Abox.Infrastructure.Dotnet;

/// <summary>The host's .NET store convention, independent of abox's config directory.</summary>
internal sealed class DotnetUserSecretStores(string? appData, string? home) : IHostUserSecretStores
{
    public static DotnetUserSecretStores Create() =>
        new(
            Environment.GetEnvironmentVariable("APPDATA"),
            Environment.GetEnvironmentVariable("HOME")
        );

    public async Task<HostUserSecretStore> ReadAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            return HostUserSecretStore.Read(
                await File.ReadAllTextAsync(
                    StorePath(id),
                    new UTF8Encoding(false, true),
                    cancellationToken
                )
            );
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return HostUserSecretStore.Missing;
        }
        catch (DecoderFallbackException)
        {
            return HostUserSecretStore.InvalidUtf8;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return HostUserSecretStore.Unreadable;
        }
    }

    private string StorePath(string id)
    {
        UserSecretId.Validate(id);
        // The configured store root is trusted; profiles above it may legitimately be linked.
        var root = StoreRoot();
        EnsureOrdinaryEntry(root);
        var store = Path.Combine(root, id);
        EnsureOrdinaryEntry(store);
        var path = Path.Combine(store, "secrets.json");
        EnsureOrdinaryEntry(path, file: true);
        return path;
    }

    public IReadOnlyList<string> StoreIds(CancellationToken cancellationToken)
    {
        try
        {
            var root = StoreRoot();
            EnsureOrdinaryEntry(root);
            return Directory
                .EnumerateDirectories(root)
                .Select(path =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Path.GetFileName(path);
                })
                .Order(StringComparer.OrdinalIgnoreCase)
                .ThenBy(id => id, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
            when (exception is DirectoryNotFoundException or FileNotFoundException)
        {
            return [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                "Cannot enumerate host user-secret stores. Check the directory and its permissions; no stores were copied."
            );
        }
    }

    private static void EnsureOrdinaryEntry(string path, bool file = false)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new AboxException(
                "Host user-secret paths must not contain a symbolic link or reparse point at or below the user-secret root; no stores were copied."
            );
        var expectedDirectory = !file;
        if (
            (attributes & FileAttributes.Device) != 0
            || attributes.HasFlag(FileAttributes.Directory) != expectedDirectory
        )
            throw new AboxException(
                "Host user-secret paths must use ordinary directories and files; no stores were copied."
            );
    }

    private string StoreRoot()
    {
        var root = appData ?? home;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new AboxException(
                "Cannot resolve the host user-secret directory. Set an absolute APPDATA or HOME; APPDATA takes precedence, including on Unix."
            );
        return appData is not null
            ? Path.Combine(root, "Microsoft", "UserSecrets")
            : Path.Combine(root, ".microsoft", "usersecrets");
    }
}
