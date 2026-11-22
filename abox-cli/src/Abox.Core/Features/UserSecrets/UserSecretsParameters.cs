using Abox.Features.Shared.CommandLine;

namespace Abox.Features.UserSecrets;

/// <summary>
/// The parameters of <c>abox user-secrets</c>. Binding has checked that the location has a
/// folder configuration, which the command requires.
/// </summary>
internal sealed record UserSecretsParameters(SandboxLocation Location, bool Force);
