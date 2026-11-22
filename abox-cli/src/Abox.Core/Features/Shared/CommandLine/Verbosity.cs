namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// The <c>--verbose</c> option of the command line: whether abox shows the complete
/// captured output of the external tools it runs. The root command sets it for each
/// invocation.
/// </summary>
public sealed class Verbosity
{
    /// <summary>Whether <c>--verbose</c> was given.</summary>
    public bool Verbose { get; set; }
}
