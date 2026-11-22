using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.UserSecrets;

/// <summary>
/// The parameters of <c>abox user-secrets</c>. <see cref="FolderConfiguration"/> is the folder
/// configuration of the location, which the command requires.
/// </summary>
internal sealed record UserSecretsParameters(
    SandboxLocation Location,
    FolderConfig FolderConfiguration,
    bool Force
);
