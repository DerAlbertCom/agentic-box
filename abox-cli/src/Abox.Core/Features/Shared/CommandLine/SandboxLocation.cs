using Abox.Features.Shared.Configuration;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// The sandbox location of one invocation: its full path and the folder configuration
/// there, loaded once, or none when the location has none.
/// </summary>
internal sealed record SandboxLocation(string FullPath, FolderConfig? FolderConfiguration);
