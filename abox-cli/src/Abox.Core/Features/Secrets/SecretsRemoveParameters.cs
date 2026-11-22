using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Secrets;

/// <summary>The parameters of <c>abox secrets remove</c>. No services means all declared ones.</summary>
internal sealed record SecretsRemoveParameters(
    SandboxLocation Location,
    IReadOnlyList<string> Services
);
