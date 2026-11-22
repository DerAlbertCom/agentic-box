namespace Abox.Features.Shared.Sandboxes;

/// <summary>Runs a command in a sandbox, starting the sandbox when it is stopped.</summary>
public interface ISandboxExec
{
    /// <summary>
    /// Runs the command attached to this console. Every command runs behind the protected
    /// HTTPS bootstrap: when the sandbox has a protected HTTPS runtime, the certificate and
    /// Node trust environment are set first, and a refusing bootstrap fails closed without
    /// running the command. Without a runtime the command runs unchanged.
    /// </summary>
    Task<SandboxExecOutcome> RunAsync(
        SandboxExecRequest request,
        CancellationToken cancellationToken
    );
}

/// <summary>How the command is connected to the console of abox.</summary>
public enum SandboxExecInput
{
    /// <summary>The command prints to the console but reads no input.</summary>
    None,

    /// <summary>The command reads the standard input of abox, as in a pipe.</summary>
    Forwarded,

    /// <summary>The command gets an interactive terminal that reads the standard input of abox.</summary>
    Terminal,
}

/// <summary>A command to run in a sandbox.</summary>
/// <param name="Sandbox">The sandbox to run in.</param>
/// <param name="Command">The command and its arguments.</param>
/// <param name="Input">How the command is connected to the console.</param>
/// <param name="WorkingDirectory">The directory to run in, or null for the image's own.</param>
/// <param name="Environment">Variables set for the command, or null for none.</param>
public sealed record SandboxExecRequest(
    string Sandbox,
    IReadOnlyList<string> Command,
    SandboxExecInput Input = SandboxExecInput.None,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null
);

/// <summary>How the command ended.</summary>
/// <param name="ExitCode">
/// The exit code abox returns for the run: the command's own, or, when the HTTPS
/// protection refused to start the command, the code of that refusal. The two cannot
/// be told apart: the refusal ends the run in the command's place.
/// </param>
public sealed record SandboxExecOutcome(int ExitCode)
{
    public bool Succeeded => ExitCode == 0;
}
