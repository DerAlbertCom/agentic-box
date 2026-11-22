using Abox.Features.Shared.Configuration;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// The sandbox location of one invocation: its full path and the folder configuration
/// there, loaded once, or none when the location has none.
/// </summary>
internal sealed class SandboxLocation(
    string fullPath,
    Func<CancellationToken, Task<FolderConfig?>> loadFolderConfiguration
)
{
    private Task<FolderConfig?>? _folderConfiguration;

    public string FullPath { get; } = fullPath;

    /// <summary>
    /// The folder configuration of the location. The first call loads it; every later call
    /// returns that result.
    /// </summary>
    public Task<FolderConfig?> FolderConfigurationAsync(CancellationToken cancellationToken) =>
        _folderConfiguration ??= loadFolderConfiguration(cancellationToken);
}
