using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Sandbox;

/// <summary>
/// The parameters of <c>abox exec</c> and <c>abox pipe</c>: the sandbox, the command after
/// the "--" (never empty) and how the standard input reaches it.
/// </summary>
internal sealed record SandboxExecParameters(
    string Sandbox,
    IReadOnlyList<string> Command,
    SandboxExecInput Input
);
