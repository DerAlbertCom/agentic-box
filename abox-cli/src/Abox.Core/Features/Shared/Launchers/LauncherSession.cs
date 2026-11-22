using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Shared.Launchers;

/// <summary>
/// What a launcher gets when it starts: where it runs and the only ways it reaches the
/// sandbox.
/// </summary>
public sealed class LauncherSession
{
    private readonly ISandboxExec exec;
    private readonly SandboxHttps https;
    private readonly IReadOnlyDictionary<string, string> environment;

    internal LauncherSession(
        string location,
        string sandboxName,
        string workspacePath,
        IReadOnlyList<string> argv,
        IReadOnlyDictionary<string, string> environment,
        ISandboxExec exec,
        SandboxHttps https
    )
    {
        Location = location;
        SandboxName = sandboxName;
        WorkspacePath = workspacePath;
        Argv = argv;
        this.environment = environment;
        this.exec = exec;
        this.https = https;
    }

    /// <summary>The folder the sandbox works on.</summary>
    public string Location { get; }

    /// <summary>The name of the sandbox.</summary>
    public string SandboxName { get; }

    /// <summary>Where the workspace is inside the sandbox.</summary>
    public string WorkspacePath { get; }

    /// <summary>The arguments after <c>--</c>.</summary>
    public IReadOnlyList<string> Argv { get; }

    /// <summary>Runs a command in the sandbox with the session's variables and returns its exit code.</summary>
    public async Task<int> ExecAsync(
        IReadOnlyList<string> command,
        CancellationToken cancellationToken,
        SandboxExecInput input = SandboxExecInput.None,
        string? workingDirectory = null
    ) =>
        (
            await exec.RunAsync(
                new SandboxExecRequest(SandboxName, command, input, workingDirectory, environment),
                cancellationToken
            )
        ).ExitCode;

    /// <summary>Fails before anything starts when protected HTTPS refuses the launch.</summary>
    public Task EnsureLaunchReadyAsync(CancellationToken cancellationToken) =>
        https.EnsureLaunchReadyAsync(SandboxName, cancellationToken);
}
