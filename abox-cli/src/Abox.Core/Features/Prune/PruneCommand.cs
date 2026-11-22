using System.CommandLine;
using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Prune;

/// <summary>
/// <c>abox prune</c>: removes the sbx template images that stopped being used.
/// </summary>
internal sealed class PruneCommand : AboxCommand<PruneParameters>
{
    private readonly Option<bool> _full = new("--full")
    {
        Description =
            "Reach for everything sbx holds, not only the stale sets. What a sandbox uses, sbx keeps.",
    };
    private readonly Option<bool> _stop = new("--sbx")
    {
        Description = "Stop the running sandboxes before the prune.",
    };

    public PruneCommand(CommandRunner runner)
        : base(
            "prune",
            "Remove the stale template images of sbx: everything but the newest set and its kit sources.",
            runner
        )
    {
        Options.Add(_full);
        Options.Add(_stop);
    }

    internal override Task<PruneParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new PruneParameters(parseResult.GetValue(_full), parseResult.GetValue(_stop))
        );
}
