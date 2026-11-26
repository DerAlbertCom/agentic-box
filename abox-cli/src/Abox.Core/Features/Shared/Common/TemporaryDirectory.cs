namespace Abox.Features.Shared.Common;

/// <summary>A temporary working directory whose cleanup failures are warnings.</summary>
internal sealed class TemporaryDirectory : IDisposable
{
    private readonly IConsole _console;
    private readonly string _purpose;

    public TemporaryDirectory(string prefix, string purpose, IConsole console)
    {
        _console = console;
        _purpose = purpose;
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _console.WriteError(
                $"warning: could not remove {_purpose} directory '{Path}': {exception.Message}"
            );
        }
    }
}
