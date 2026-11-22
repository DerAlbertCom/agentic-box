namespace Abox.Features.Shared.Https;

/// <summary>
/// Protected development HTTPS inside one Sandbox. Each operation reports how it ended; no
/// runtime detail, child diagnostic or private material crosses this interface.
/// </summary>
public interface ISandboxDevelopmentCertificates
{
    /// <summary>Prepares the protected runtime and returns the password the export must use.</summary>
    Task<SandboxCertificatePreparation> PrepareAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>Trusts the public certificate in the sandbox, then activates the private one.</summary>
    Task<SandboxCertificateActivation> ActivateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    );

    /// <summary>Confirms fresh launches and existing agents use the activated certificate.</summary>
    Task<SandboxCertificateStatus> VerifyActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>Refreshes only the verified reader of an existing protected HTTPS runtime.</summary>
    Task<SandboxCertificateStatus> RefreshBootstrapAsync(
        string sandbox,
        CancellationToken cancellationToken
    );
}

/// <summary>How a protected HTTPS operation in a sandbox ended.</summary>
public enum SandboxCertificateStatus
{
    Succeeded,

    /// <summary>The operation was canceled while it ran.</summary>
    Canceled,

    /// <summary>The operation could not be run in the sandbox.</summary>
    Unavailable,

    /// <summary>The protected runtime stayed locked by another operation.</summary>
    Busy,

    /// <summary>The operation ran and refused.</summary>
    Failed,

    /// <summary>Activation is in place, but existing Herdr or agent parents are stale.</summary>
    StaleParents,

    /// <summary>The process environments of existing agents cannot be inspected.</summary>
    ProcessEnvironmentsUnavailable,

    /// <summary>The Node extra-CA configuration could not be completed.</summary>
    NodeExtraCaIncomplete,
}

/// <summary>The prepared runtime; <see cref="Password"/> is set only when it succeeded.</summary>
public sealed record SandboxCertificatePreparation(
    SandboxCertificateStatus Status,
    string Password = ""
)
{
    public override string ToString() => $"SandboxCertificatePreparation {{ Status = {Status} }}";
}

/// <summary>How an activation ended, and whether a failed transfer's cleanup was confirmed.</summary>
public sealed record SandboxCertificateActivation(
    SandboxCertificateActivationStatus Status,
    bool CleanupConfirmed = true
);

public enum SandboxCertificateActivationStatus
{
    Activated,

    /// <summary>Canceled while the public certificate was being trusted.</summary>
    TrustCanceled,

    /// <summary>The public certificate could not be trusted in the sandbox.</summary>
    TrustFailed,

    /// <summary>The certificate or password cannot be transferred; nothing was sent.</summary>
    InvalidPayload,

    /// <summary>Canceled while the private certificate was being transferred.</summary>
    TransferCanceled,

    /// <summary>The protected runtime stayed locked during the transfer.</summary>
    TransferBusy,

    /// <summary>The private certificate could not be transferred.</summary>
    TransferFailed,
}
