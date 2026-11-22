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
}
