using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Run;

/// <summary>Runs <c>abox run</c>: runs preflight, then starts the launcher.</summary>
internal sealed class RunHandler(Preflight preflight, RunFlow run) : ICommandHandler<RunParameters>
{
    public async Task<int> HandleAsync(
        RunParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var request = new RunRequest(
            parameters.LauncherId,
            parameters.Location,
            parameters.Argv,
            parameters.TerminalTheme
        );

        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        return await run.RunAsync(request, cancellationToken);
    }
}
