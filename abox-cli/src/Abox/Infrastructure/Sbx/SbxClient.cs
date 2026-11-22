using Abox.Features.Shared.Configuration;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// The calls abox makes to <c>sbx</c> that are not yet behind a use-case interface.
/// </summary>
public sealed class SbxClient : ISbxClient
{
    private readonly SbxInvoker _invoker;
    private readonly SbxDevelopmentCertificates _certificates;

    public SbxClient(IProcessRunner runner, AboxPaths paths)
    {
        _invoker = new SbxInvoker(runner);
        _certificates = new SbxDevelopmentCertificates(_invoker);
    }

    public Task ActivateDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    ) =>
        _certificates.ActivateDevelopmentCertificateAsync(
            sandbox,
            pfx,
            password,
            cancellationToken
        );

    public Task<string> GetDevelopmentCertificatePasswordAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => _certificates.GetDevelopmentCertificatePasswordAsync(sandbox, cancellationToken);

    public Task VerifyDevelopmentCertificateActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => _certificates.VerifyDevelopmentCertificateActivationAsync(sandbox, cancellationToken);

    public Task RefreshDevelopmentCertificateBootstrapAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => _certificates.RefreshDevelopmentCertificateBootstrapAsync(sandbox, cancellationToken);
}
