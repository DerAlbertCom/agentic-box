using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Secrets;

/// <summary>The parameters of <c>abox secrets set</c>. No services means all declared ones.</summary>
internal sealed record SecretsSetParameters(
    SandboxLocation Location,
    IReadOnlyList<string> Services
);
