using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Secrets;

/// <summary>The parameters of <c>abox secrets list</c>.</summary>
internal sealed record SecretsListParameters(SandboxLocation Location);
