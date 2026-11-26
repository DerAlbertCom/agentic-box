namespace Abox.Features.Shared.Sandboxes;

/// <summary>Everything a new sandbox is made of.</summary>
/// <param name="KitSource">The workload kit: a local kit directory or published set reference.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Name">The sandbox name.</param>
/// <param name="Mixins">The mixin kits, as local directories or registry references.</param>
/// <param name="KitArguments">The values for the kits' arguments.</param>
/// <param name="Mounts">The host directories mounted into the sandbox.</param>
/// <param name="StaticMcpServers">The MCP servers the sandbox always carries.</param>
/// <param name="PullPolicy">Whether to refresh registry kits or use only local templates.</param>
/// <param name="HostImageStore">The host image store holding a prebuilt local workload; the kits are then built on the host.</param>
/// <param name="Progress">Receives each line the runtime writes while it creates, in arrival order, with known secrets redacted.</param>
/// <param name="Memory">The memory of the sandbox in GB; null leaves the runtime's default.</param>
/// <param name="Cpus">The CPUs of the sandbox; null leaves the runtime's default.</param>
public sealed record SandboxDefinition(
    string KitSource,
    string Location,
    string Name,
    IReadOnlyList<string> Mixins,
    IReadOnlyDictionary<string, string> KitArguments,
    IReadOnlyList<SandboxMount> Mounts,
    IReadOnlyList<string> StaticMcpServers,
    PullPolicy PullPolicy = PullPolicy.Never,
    string? HostImageStore = null,
    Action<string>? Progress = null,
    int? Memory = null,
    int? Cpus = null
);
