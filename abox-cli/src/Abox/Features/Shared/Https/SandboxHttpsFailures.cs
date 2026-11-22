namespace Abox.Features.Shared.Https;

/// <summary>The actionable failure of each sandbox HTTPS step, rendered from its outcome.</summary>
internal static class SandboxHttpsFailures
{
    private const string Busy =
        "Protected HTTPS runtime is busy after a bounded lock wait; retry the operation or 'abox ports'. No complete readiness is claimed. Child diagnostics were suppressed. ";
    private const string PreparationFailure =
        "Cannot prepare protected HTTPS runtime configuration. Check Python 3.8+, non-root execution and owner-only nonmounted /tmp paths. Child diagnostics were suppressed.";
    private const string RefreshFailure =
        "Cannot refresh the protected HTTPS bootstrap. Require an existing valid private activation, Python 3.8+, non-root execution and ordinary owner-only nonmounted runtime files (including config.json, active.pfx and public.pem). Correct protected-state ownership/integrity or rerun abox ports for full setup. No host export, root trust or port change was attempted; child diagnostics were suppressed.";

    internal static void ThrowIfPreparationFailed(
        SandboxCertificateStatus status,
        CancellationToken token
    )
    {
        switch (status)
        {
            case SandboxCertificateStatus.Succeeded:
                return;
            case SandboxCertificateStatus.Canceled:
                throw new OperationCanceledException("HTTPS runtime preparation canceled.", token);
            case SandboxCertificateStatus.Busy:
                throw new AboxException(Busy);
            default:
                throw new AboxException(PreparationFailure);
        }
    }

    internal static void ThrowIfActivationFailed(
        SandboxCertificateActivation activation,
        CancellationToken token
    )
    {
        switch (activation.Status)
        {
            case SandboxCertificateActivationStatus.Activated:
                return;
            case SandboxCertificateActivationStatus.TrustCanceled:
                throw new OperationCanceledException(
                    "Sandbox trust setup canceled; trust may be partially updated. Retry abox ports; no complete activation is claimed.",
                    token
                );
            case SandboxCertificateActivationStatus.TrustFailed:
                throw new AboxException(
                    "Cannot establish exact public development-certificate sandbox trust. Require sandbox root exec, Python 3.8+, an existing Debian-style ca-certificates store and /usr/sbin/update-ca-certificates on ordinary root-owned, nonmounted paths. No packages were installed and host trust was not changed. Correct the sandbox prerequisite and retry abox ports; trust may be partially updated, no complete activation is claimed. Child diagnostics were suppressed."
                );
            case SandboxCertificateActivationStatus.InvalidPayload:
                throw new AboxException(
                    "Invalid certificate transfer payload; no sandbox operation was started."
                );
            case SandboxCertificateActivationStatus.TransferCanceled:
                throw new OperationCanceledException(
                    activation.CleanupConfirmed
                        ? "Certificate transfer canceled; retry after checking runtime state."
                        : "Certificate transfer canceled; remote temporary cleanup is unconfirmed. Remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying.",
                    token
                );
            default:
                var failure =
                    activation.Status == SandboxCertificateActivationStatus.TransferBusy
                        ? Busy
                        : "Cannot transfer the development certificate. Check Python 3.8+, the non-root application user, ordinary owner-only runtime paths and mount layout. Child diagnostics were suppressed. ";
                var recovery = activation.CleanupConfirmed
                    ? "Retry after correcting the destination; an existing active certificate is retained unless the completed commit was not acknowledged. Initial protected configuration may already be present; no rollback is promised."
                    : "Remote temporary cleanup is unconfirmed; remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying.";
                throw new AboxException(failure + recovery);
        }
    }

    internal static void ThrowIfVerificationFailed(
        SandboxCertificateStatus status,
        CancellationToken token
    )
    {
        switch (status)
        {
            case SandboxCertificateStatus.Succeeded:
                return;
            case SandboxCertificateStatus.Canceled:
                throw new OperationCanceledException("HTTPS activation check canceled.", token);
            case SandboxCertificateStatus.Unavailable:
                throw new AboxException(
                    "Cannot verify protected HTTPS launch configuration; child diagnostics were suppressed."
                );
            case SandboxCertificateStatus.StaleParents:
                throw new AboxException(
                    "HTTPS certificate copied, but existing Herdr/agent parents are stale: full activation is NOT complete. Explicitly exit existing Copilot/OpenCode/pi/Claude Code parents and open fresh ones through abox. Save work and obtain consent before stopping core-managed sessions. For an existing Herdr core, deliberately run 'abox run shell -- hc server restart --fresh' if supported by installed hc; otherwise explicitly run 'abox run shell -- hc server stop', then 'abox run shell -- hc server start'. For a stale OpenCode service, explicitly run 'abox run shell -- opencode service stop', then reopen it through abox. Nothing was restarted automatically. Then retry 'abox ports'. Already-running apps require explicit restart for renewal."
                );
            case SandboxCertificateStatus.ProcessEnvironmentsUnavailable:
                throw new AboxException(
                    "Cannot confirm existing agent HTTPS readiness: sandbox /proc process environments are not inspectable (permissions or process visibility). Ordinary shell/exec/pipe and recovery remain available; no existing agent is claimed configured and nothing was restarted."
                );
            case SandboxCertificateStatus.NodeExtraCaIncomplete:
                throw new AboxException(
                    "HTTPS trust/private activation may be present, but Node extra-CA configuration is incomplete. Check NODE_EXTRA_CA_CERTS: require a user-readable regular PEM file (caller-selected symlinks are supported) of at most 1 MiB and no more than 16 distinct source-content variants. Ordinary/recovery access retains the original Node setting and protected Kestrel configuration. Correct the input or inspect unused owned node pairs and retry abox ports. No complete activation is claimed."
                );
            case SandboxCertificateStatus.Busy:
                throw new AboxException(Busy);
            default:
                throw new AboxException(
                    "Cannot verify protected HTTPS launch configuration. Check runtime file ownership, permissions, public.pem, owned Node source integrity and process visibility; no complete activation is claimed."
                );
        }
    }

    internal static void ThrowIfRefreshFailed(
        SandboxCertificateStatus status,
        CancellationToken token
    )
    {
        switch (status)
        {
            case SandboxCertificateStatus.Succeeded:
                return;
            case SandboxCertificateStatus.Canceled:
                throw new OperationCanceledException(
                    "HTTPS bootstrap refresh canceled; the atomic reader replacement may already be committed. Retry the explicit refresh; HTTPS readiness was not verified.",
                    token
                );
            case SandboxCertificateStatus.Busy:
                throw new AboxException(Busy);
            default:
                throw new AboxException(RefreshFailure);
        }
    }
}
