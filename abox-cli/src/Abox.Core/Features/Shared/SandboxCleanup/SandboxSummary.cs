namespace Abox.Features.Shared.SandboxCleanup;

/// <summary>A sandbox the runtime knows about, as far as a cleanup needs it.</summary>
/// <param name="Name">The sandbox name.</param>
/// <param name="IsRunning">Whether the sandbox is running.</param>
public sealed record SandboxSummary(string Name, bool IsRunning);
