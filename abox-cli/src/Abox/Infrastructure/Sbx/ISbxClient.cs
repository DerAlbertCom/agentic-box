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

    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    /// <summary>Where the runtime put the workspace of the sandbox, when it says.</summary>
    Task<string?> GetWorkspaceDirectoryAsync(string sandbox, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken);

    Task<SbxOutcome> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    );
}
