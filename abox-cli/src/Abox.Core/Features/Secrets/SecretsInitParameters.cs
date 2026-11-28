using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Secrets;

/// <summary>
/// The parameters of <c>abox secrets init</c>. <see cref="Squad"/> is the given one; the
/// folder configuration may still name it.
/// </summary>
internal sealed record SecretsInitParameters(
    SandboxLocation Location,
    string Team,
    string? Squad,
    string Tag,
    bool Local,
    bool All
);
