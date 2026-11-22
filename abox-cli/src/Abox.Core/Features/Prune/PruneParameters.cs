namespace Abox.Features.Prune;

/// <summary>The parameters of <c>abox prune</c>: its <c>--full</c> and <c>--sbx</c> switches.</summary>
internal sealed record PruneParameters(bool Full, bool StopSandboxes);
