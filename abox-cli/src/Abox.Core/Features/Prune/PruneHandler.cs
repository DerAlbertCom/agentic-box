using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Prune;

/// <summary>Runs <c>abox prune</c>: runs preflight, then prunes the template images.</summary>
internal sealed class PruneHandler(Preflight preflight, PruneService prune)
    : ICommandHandler<PruneParameters>
{
    public async Task<int> HandleAsync(
        PruneParameters parameters,
        CancellationToken cancellationToken
    )
    {
        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        return await prune.PruneTemplatesAsync(
            new PruneRequest(parameters.Full, parameters.StopSandboxes),
            cancellationToken
        );
    }
}
