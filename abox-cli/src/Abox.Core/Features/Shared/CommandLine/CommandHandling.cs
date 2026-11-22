using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// Turns abox errors into exit code 1 and cancellation into exit code 130, the same way the
/// command runner does for actions. The entry point guards the composition with it.
/// </summary>
public static class CommandHandling
{
    public static async Task<int> RunAsync(Func<Task<int>> work, IConsole console)
    {
        try
        {
            return await work();
        }
        catch (AboxException exception)
        {
            console.WriteError($"error: {exception.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            console.WriteError("error: the command was cancelled.");
            return 130;
        }
    }
}
