namespace Abox.Features.Shared.Credentials;

/// <summary>
/// Runs the <c>valueCommand</c> of a kit's secret on the host, with a POSIX shell, and
/// returns what it printed: the value of the secret, so it is never shown.
/// </summary>
public interface IHostCommands
{
    /// <summary>Runs the command line and captures its standard output.</summary>
    Task<HostCommandResult> RunAsync(string commandLine, CancellationToken cancellationToken);
}

/// <summary>How a host command ended.</summary>
/// <param name="ExitCode">The exit code; 0 is success.</param>
/// <param name="Output">What the command printed on its standard output.</param>
public sealed record HostCommandResult(int ExitCode, string Output)
{
    /// <summary>Whether the command succeeded.</summary>
    public bool Succeeded => ExitCode == 0;
}
