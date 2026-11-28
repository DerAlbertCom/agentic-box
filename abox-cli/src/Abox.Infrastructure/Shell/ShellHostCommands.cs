using Abox.Features.Shared.Common;
using Abox.Features.Shared.Credentials;

namespace Abox.Infrastructure.Shell;

/// <summary>
/// Runs the value command of a kit's secret with a POSIX shell, as <c>sh -c</c>: on Windows
/// the <c>sh</c> of Git for Windows, as for a kit's build file. The output is the value of
/// a secret, so it is captured and never shown.
/// </summary>
internal sealed class ShellHostCommands(IProcessRunner runner, Func<string?> findWindowsShell)
    : IHostCommands
{
    public ShellHostCommands(IProcessRunner runner)
        : this(runner, GitForWindowsShell.Find) { }

    public async Task<HostCommandResult> RunAsync(
        string commandLine,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(Request(commandLine), cancellationToken);
        return new HostCommandResult(result.ExitCode, result.StandardOutput);
    }

    private ProcessRequest Request(string commandLine)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new ProcessRequest(
                "/bin/sh",
                ["-c", commandLine],
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            );
        }

        var shell =
            findWindowsShell()
            ?? throw new AboxException(
                $"The host command '{commandLine}' of a secret runs on Windows with the sh of Git for Windows, and none was found. Install Git for Windows, or put the folder of its sh.exe on the PATH."
            );
        return new ProcessRequest(
            shell,
            ["-c", commandLine],
            Environment: new Dictionary<string, string>
            {
                // sh.exe started from outside Git Bash does not have its own tools on the PATH.
                ["PATH"] =
                    Path.GetDirectoryName(shell)
                    + Path.PathSeparator
                    + System.Environment.GetEnvironmentVariable("PATH"),
            },
            OutputPolicy: ProcessOutputPolicy.CapturedSensitive
        );
    }
}
