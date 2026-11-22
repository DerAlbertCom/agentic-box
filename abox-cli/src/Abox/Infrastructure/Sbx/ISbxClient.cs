using Abox.Features.Shared.Squads;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// What abox asks of sbx. The interface keeps the callers testable and the wrapper
/// class the only place that knows the flags.
/// </summary>
public interface ISbxClient
{
    Task<string> GetDevelopmentCertificatePasswordAsync(
        string sandbox,
        CancellationToken cancellationToken
    );
    Task ActivateDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    );
    Task VerifyDevelopmentCertificateActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>Refreshes only the verified reader of an existing protected HTTPS runtime.</summary>
    Task RefreshDevelopmentCertificateBootstrapAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(CancellationToken cancellationToken);

    Task<SbxOutcome> CreateAsync(SbxCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Adds a validated repository prefix to kit.allowedSources if it is not already covered.</summary>
    Task EnsureKitSourceAllowedAsync(string repositoryPrefix, CancellationToken cancellationToken);

    Task<SbxOutcome> RemoveSandboxAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Reads every staged kit descriptor and the bundled squad metadata of a sandbox in one
    /// exec, starting the sandbox when it is stopped.
    /// </summary>
    Task<SandboxSquadFiles> ReadSquadFilesAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    /// <summary>Where the runtime put the workspace of the sandbox, when it says.</summary>
    Task<string?> GetWorkspaceDirectoryAsync(string sandbox, CancellationToken cancellationToken);

    Task<SbxOutcome> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? arguments = null
    );

    Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken);

    Task<SbxOutcome> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    );

    Task<HostSecrets> ListSecretsAsync(CancellationToken cancellationToken);

    Task<SbxOutcome> SetSecretAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    );

    Task<SbxOutcome> SetCustomSecretAsync(
        SecretDeclaration declaration,
        string? value,
        CancellationToken cancellationToken
    );

    Task<SbxOutcome> RemoveSecretAsync(
        SecretDeclaration declaration,
        CancellationToken cancellationToken
    );
}
