using Abox.Features.Shared.Common;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Shared.Https;

/// <summary>
/// Protected development HTTPS for one Sandbox: full configuration, the bootstrap-only
/// refresh and the launch-readiness check. The only production caller of the sandbox
/// certificate operations.
/// </summary>
internal sealed class SandboxHttps(
    IHostDevelopmentCertificate exporter,
    ISandboxDevelopmentCertificates certificates,
    ISandboxExec exec,
    IConsole console
)
{
    public async Task ConfigureAsync(string sandbox, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var preparation = await certificates.PrepareAsync(sandbox, token);
        SandboxHttpsFailures.ThrowIfPreparationFailed(preparation.Status, token);
        using var certificate = await exporter.ExportAsync(preparation.Password, token);
        token.ThrowIfCancellationRequested();
        SandboxHttpsFailures.ThrowIfActivationFailed(
            await certificates.ActivateAsync(sandbox, certificate.Pfx, certificate.Password, token),
            token
        );
        token.ThrowIfCancellationRequested();
        SandboxHttpsFailures.ThrowIfVerificationFailed(
            await certificates.VerifyActivationAsync(sandbox, token),
            token
        );
        token.ThrowIfCancellationRequested();
        console.WriteLine(
            "HTTPS default certificate and exact public sandbox system trust configured for fresh abox shell/direct/exec/pipe processes and their new agent children. Node extra-CA roots are preserved and merged at launch; unreadable/conflicting trust input fails actionably rather than being discarded. Existing apps/Node clients must be explicitly restarted for renewal; existing parents must be fresh. Custom endpoint certificate settings can override the default. Host/browser trust was not tested by this setup."
        );
    }

    /// <summary>Refreshes only the verified reader; no host export, root trust or port change.</summary>
    public async Task RefreshBootstrapAsync(string sandbox, CancellationToken token)
    {
        SandboxHttpsFailures.ThrowIfRefreshFailed(
            await certificates.RefreshBootstrapAsync(sandbox, token),
            token
        );
        console.WriteLine(
            "HTTPS bootstrap refreshed from this CLI. Private activation retained; HTTPS/Node/agent readiness was not verified. Retry the original launch; use abox ports for full setup."
        );
    }

    /// <summary>
    /// Crosses the protected HTTPS bootstrap once, so a launch fails before anything starts
    /// when the bootstrap refuses.
    /// </summary>
    public async Task EnsureLaunchReadyAsync(string sandbox, CancellationToken token)
    {
        if (!(await exec.RunAsync(new SandboxExecRequest(sandbox, ["true"]), token)).Succeeded)
            throw new AboxException(
                "Herdr was not opened: sandbox HTTPS launch readiness failed. Exit stale agents and explicitly initialize fresh core/service parents through abox after saving work; rerun 'abox ports' for recovery guidance. No core or app was restarted."
            );
    }
}
