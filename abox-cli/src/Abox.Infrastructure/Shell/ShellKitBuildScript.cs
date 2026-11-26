using Abox.Features.Shared.Common;
using Abox.Features.Shared.KitBuildScripts;

namespace Abox.Infrastructure.Shell;

/// <summary>
/// Runs a kit's build file as <c>build &lt;stagedKitDir&gt;</c> in the kit's folder. Windows
/// has no executable files, so there the build file runs with the <c>sh</c> of Git for
/// Windows, with its path conversion turned off so Docker volume arguments reach Docker as
/// written.
/// </summary>
internal sealed class ShellKitBuildScript(IProcessRunner runner, Func<string?> findWindowsShell)
    : IKitBuildScript
{
    public ShellKitBuildScript(IProcessRunner runner)
        : this(runner, GitForWindowsShell.Find) { }

    public async Task RunAsync(KitBuildScriptRun run, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(Request(run), cancellationToken);
        if (!result.Succeeded)
        {
            throw new AboxException(
                $"The build of kit '{run.KitName}' failed with exit code {result.ExitCode}:{Environment.NewLine}{(result.StandardOutput + result.StandardError).Trim()}"
            );
        }
    }

    private ProcessRequest Request(KitBuildScriptRun run)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ProcessRequest(
                run.BuildFile,
                [run.StagedDirectory],
                WorkingDirectory: run.KitDirectory
            );
        }

        var shell =
            findWindowsShell()
            ?? throw new AboxException(
                $"The kit '{run.KitName}' has a build step ({run.BuildFile}), which runs on Windows with the sh of Git for Windows, and none was found. Install Git for Windows, or put the folder of its sh.exe on the PATH."
            );
        return new ProcessRequest(
            shell,
            [ForwardSlashes(run.BuildFile), ForwardSlashes(run.StagedDirectory)],
            WorkingDirectory: run.KitDirectory,
            Environment: new Dictionary<string, string>
            {
                // sh.exe started from outside Git Bash does not have its own tools on the PATH.
                ["PATH"] =
                    Path.GetDirectoryName(shell)
                    + Path.PathSeparator
                    + Environment.GetEnvironmentVariable("PATH"),
                ["MSYS_NO_PATHCONV"] = "1",
                ["MSYS2_ARG_CONV_EXCL"] = "*",
            }
        );
    }

    private static string ForwardSlashes(string path) => path.Replace('\\', '/');
}
