using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Shell;

/// <summary>
/// The real path of a folder as <c>bash</c>'s <c>pwd -P</c> prints it; on Windows, the
/// final link target of the full path.
/// </summary>
public sealed class ShellFolderRealPath(IProcessRunner runner) : IFolderRealPath
{
    public async Task<string> ResolveAsync(string location, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var full = Path.GetFullPath(location);
            try
            {
                return Directory.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                return full;
            }
        }

        var result = await runner.RunAsync(
            new ProcessRequest("bash", ["-c", "cd \"$1\" 2>/dev/null && pwd -P", "bash", location]),
            cancellationToken
        );
        var output = result.StandardOutput.TrimEnd('\r', '\n');
        return result.ExitCode == 0 && output.Length > 0 ? output.Split('\n')[^1] : location;
    }
}
