namespace Abox.Infrastructure;

/// <summary>
/// What a finished process left behind.
/// </summary>
internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    private readonly string? _output;

    public bool Succeeded => ExitCode == 0;

    /// <summary>
    /// Both streams in the order their lines arrived; standard output followed by standard
    /// error when the order is not known.
    /// </summary>
    public string Output
    {
        get => _output ?? StandardOutput + StandardError;
        init => _output = value;
    }
}
