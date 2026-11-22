namespace Abox.Features.Shared.Sandboxes;

/// <summary>A host directory that is mounted into the sandbox.</summary>
public sealed record SandboxMount(string HostPath, string SandboxPath);
