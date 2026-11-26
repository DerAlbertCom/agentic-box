using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Create;

/// <summary>
/// The parameters of <c>abox create</c>. <see cref="Memory"/> (GB) and <see cref="Cpus"/> are
/// only what was given; null falls back to the folder configuration when the sandbox is created.
/// </summary>
internal sealed record CreateParameters(
    SandboxLocation Location,
    string Team,
    string? Squad,
    string Tag,
    bool Local,
    bool AssumeYes,
    int? Memory,
    int? Cpus
);
