using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Secrets;

/// <summary>
/// The parameters of <c>abox secrets update</c>. No services means the user picks them.
/// <see cref="Team"/> and <see cref="Tag"/> are used only when the folder has no sandbox.
/// </summary>
internal sealed record SecretsUpdateParameters(
    SandboxLocation Location,
    IReadOnlyList<string> Services,
    string? Team,
    string Tag
);
