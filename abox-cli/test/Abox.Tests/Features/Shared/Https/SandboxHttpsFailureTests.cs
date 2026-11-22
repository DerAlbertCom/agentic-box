using Abox.Features.Shared.Common;
using Abox.Features.Shared.Https;
using NSubstitute;

namespace Abox.Tests;

/// <summary>The failure texts of each sandbox HTTPS step, as abox has always printed them.</summary>
public sealed class SandboxHttpsFailureTests
{
    private const string Busy =
        "Protected HTTPS runtime is busy after a bounded lock wait; retry the operation or 'abox ports'. No complete readiness is claimed. Child diagnostics were suppressed. ";
    private const string TransferFailure =
        "Cannot transfer the development certificate. Check Python 3.8+, the non-root application user, ordinary owner-only runtime paths and mount layout. Child diagnostics were suppressed. ";
    private const string CleanedRecovery =
        "Retry after correcting the destination; an existing active certificate is retained unless the completed commit was not acknowledged. Initial protected configuration may already be present; no rollback is promised.";
    private const string UncleanedRecovery =
        "Remote temporary cleanup is unconfirmed; remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying.";

    public static TheoryData<SandboxCertificateStatus, string> PreparationFailures =>
        new()
        {
            { SandboxCertificateStatus.Busy, Busy },
            {
                SandboxCertificateStatus.Unavailable,
                "Cannot prepare protected HTTPS runtime configuration. Check Python 3.8+, non-root execution and owner-only nonmounted /tmp paths. Child diagnostics were suppressed."
            },
            {
                SandboxCertificateStatus.Failed,
                "Cannot prepare protected HTTPS runtime configuration. Check Python 3.8+, non-root execution and owner-only nonmounted /tmp paths. Child diagnostics were suppressed."
            },
        };

    [Theory]
    [MemberData(nameof(PreparationFailures))]
    public async Task PreparationFailureStopsBeforeTheHostExport(
        SandboxCertificateStatus status,
        string message
    )
    {
        // Arrange
        var scenario = new Scenario();
        scenario
            .Certificates.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCertificatePreparation(status));

        // Act
        var action = () => scenario.Https.ConfigureAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be(message);
        scenario.Exporter.ReceivedCalls().Should().BeEmpty();
    }

    public static TheoryData<SandboxCertificateActivation, string> ActivationFailures =>
        new()
        {
            {
                new(SandboxCertificateActivationStatus.TrustFailed),
                "Cannot establish exact public development-certificate sandbox trust. Require sandbox root exec, Python 3.8+, an existing Debian-style ca-certificates store and /usr/sbin/update-ca-certificates on ordinary root-owned, nonmounted paths. No packages were installed and host trust was not changed. Correct the sandbox prerequisite and retry abox ports; trust may be partially updated, no complete activation is claimed. Child diagnostics were suppressed."
            },
            {
                new(SandboxCertificateActivationStatus.InvalidPayload),
                "Invalid certificate transfer payload; no sandbox operation was started."
            },
            {
                new(SandboxCertificateActivationStatus.TransferFailed),
                TransferFailure + CleanedRecovery
            },
            {
                new(SandboxCertificateActivationStatus.TransferFailed, CleanupConfirmed: false),
                TransferFailure + UncleanedRecovery
            },
            { new(SandboxCertificateActivationStatus.TransferBusy), Busy + CleanedRecovery },
            {
                new(SandboxCertificateActivationStatus.TransferBusy, CleanupConfirmed: false),
                Busy + UncleanedRecovery
            },
        };

    [Theory]
    [MemberData(nameof(ActivationFailures))]
    public async Task ActivationFailureStopsBeforeVerification(
        SandboxCertificateActivation activation,
        string message
    )
    {
        // Arrange
        var scenario = new Scenario();
        scenario.Activation(activation);

        // Act
        var action = () => scenario.Https.ConfigureAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be(message);
        await scenario
            .Certificates.DidNotReceive()
            .VerifyActivationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(
        SandboxCertificateActivationStatus.TrustCanceled,
        true,
        "Sandbox trust setup canceled; trust may be partially updated. Retry abox ports; no complete activation is claimed."
    )]
    [InlineData(
        SandboxCertificateActivationStatus.TransferCanceled,
        true,
        "Certificate transfer canceled; retry after checking runtime state."
    )]
    [InlineData(
        SandboxCertificateActivationStatus.TransferCanceled,
        false,
        "Certificate transfer canceled; remote temporary cleanup is unconfirmed. Remove task-owned .transfer-*.tmp files from the protected runtime certificate directory before retrying."
    )]
    public async Task CanceledActivationIsACancellationOfTheCallersToken(
        SandboxCertificateActivationStatus status,
        bool cleaned,
        string message
    )
    {
        // Arrange
        var scenario = new Scenario();
        scenario.Activation(new SandboxCertificateActivation(status, cleaned));
        using var cancellation = new CancellationTokenSource();

        // Act
        var action = () => scenario.Https.ConfigureAsync("actual", cancellation.Token);

        // Assert
        var canceled = (await action.Should().ThrowAsync<OperationCanceledException>()).Which;
        canceled.Message.Should().Be(message);
        canceled.CancellationToken.Should().Be(cancellation.Token);
    }

    public static TheoryData<SandboxCertificateStatus, string> VerificationFailures =>
        new()
        {
            {
                SandboxCertificateStatus.StaleParents,
                "HTTPS certificate copied, but existing Herdr/agent parents are stale: full activation is NOT complete. Explicitly exit existing Copilot/OpenCode/pi/Claude Code parents and open fresh ones through abox. Save work and obtain consent before stopping core-managed sessions. For an existing Herdr core, deliberately run 'abox run shell -- hc server restart --fresh' if supported by installed hc; otherwise explicitly run 'abox run shell -- hc server stop', then 'abox run shell -- hc server start'. For a stale OpenCode service, explicitly run 'abox run shell -- opencode service stop', then reopen it through abox. Nothing was restarted automatically. Then retry 'abox ports'. Already-running apps require explicit restart for renewal."
            },
            {
                SandboxCertificateStatus.ProcessEnvironmentsUnavailable,
                "Cannot confirm existing agent HTTPS readiness: sandbox /proc process environments are not inspectable (permissions or process visibility). Ordinary shell/exec/pipe and recovery remain available; no existing agent is claimed configured and nothing was restarted."
            },
            {
                SandboxCertificateStatus.NodeExtraCaIncomplete,
                "HTTPS trust/private activation may be present, but Node extra-CA configuration is incomplete. Check NODE_EXTRA_CA_CERTS: require a user-readable regular PEM file (caller-selected symlinks are supported) of at most 1 MiB and no more than 16 distinct source-content variants. Ordinary/recovery access retains the original Node setting and protected Kestrel configuration. Correct the input or inspect unused owned node pairs and retry abox ports. No complete activation is claimed."
            },
            { SandboxCertificateStatus.Busy, Busy },
            {
                SandboxCertificateStatus.Unavailable,
                "Cannot verify protected HTTPS launch configuration; child diagnostics were suppressed."
            },
            {
                SandboxCertificateStatus.Failed,
                "Cannot verify protected HTTPS launch configuration. Check runtime file ownership, permissions, public.pem, owned Node source integrity and process visibility; no complete activation is claimed."
            },
        };

    [Theory]
    [MemberData(nameof(VerificationFailures))]
    public async Task VerificationFailureClaimsNoConfiguration(
        SandboxCertificateStatus status,
        string message
    )
    {
        // Arrange
        var scenario = new Scenario();
        scenario
            .Certificates.VerifyActivationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(status);

        // Act
        var action = () => scenario.Https.ConfigureAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be(message);
        scenario.Console.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SandboxCertificateStatus.Busy, Busy)]
    [InlineData(
        SandboxCertificateStatus.Unavailable,
        "Cannot refresh the protected HTTPS bootstrap. Require an existing valid private activation, Python 3.8+, non-root execution and ordinary owner-only nonmounted runtime files (including config.json, active.pfx and public.pem). Correct protected-state ownership/integrity or rerun abox ports for full setup. No host export, root trust or port change was attempted; child diagnostics were suppressed."
    )]
    [InlineData(
        SandboxCertificateStatus.Failed,
        "Cannot refresh the protected HTTPS bootstrap. Require an existing valid private activation, Python 3.8+, non-root execution and ordinary owner-only nonmounted runtime files (including config.json, active.pfx and public.pem). Correct protected-state ownership/integrity or rerun abox ports for full setup. No host export, root trust or port change was attempted; child diagnostics were suppressed."
    )]
    public async Task BootstrapRefreshFailureClaimsNoRefresh(
        SandboxCertificateStatus status,
        string message
    )
    {
        // Arrange
        var scenario = new Scenario();
        scenario
            .Certificates.RefreshBootstrapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(status);

        // Act
        var action = () => scenario.Https.RefreshBootstrapAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be(message);
        scenario.Console.Output.Should().BeEmpty();
    }

    [Theory]
    [InlineData("prepare", "HTTPS runtime preparation canceled.")]
    [InlineData("verify", "HTTPS activation check canceled.")]
    [InlineData(
        "refresh",
        "HTTPS bootstrap refresh canceled; the atomic reader replacement may already be committed. Retry the explicit refresh; HTTPS readiness was not verified."
    )]
    public async Task CanceledStepsAreACancellationOfTheCallersToken(string step, string message)
    {
        // Arrange
        var scenario = new Scenario();
        scenario
            .Certificates.PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new SandboxCertificatePreparation(
                    step == "prepare"
                        ? SandboxCertificateStatus.Canceled
                        : SandboxCertificateStatus.Succeeded
                )
            );
        scenario
            .Certificates.VerifyActivationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SandboxCertificateStatus.Canceled);
        scenario
            .Certificates.RefreshBootstrapAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SandboxCertificateStatus.Canceled);
        using var cancellation = new CancellationTokenSource();

        // Act
        var action = () =>
            step == "refresh"
                ? scenario.Https.RefreshBootstrapAsync("actual", cancellation.Token)
                : scenario.Https.ConfigureAsync("actual", cancellation.Token);

        // Assert
        var canceled = (await action.Should().ThrowAsync<OperationCanceledException>()).Which;
        canceled.Message.Should().Be(message);
        canceled.CancellationToken.Should().Be(cancellation.Token);
    }

    private sealed class Scenario
    {
        public Scenario()
        {
            Exporter
                .ExportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => new ExportedDevelopmentCertificate([1, 2, 3], "password"));
            Https = new SandboxHttps(Exporter, Certificates, SandboxFactory.Exec(), Console);
        }

        public ISandboxDevelopmentCertificates Certificates { get; } =
            SandboxFactory.Certificates();
        public IHostDevelopmentCertificate Exporter { get; } =
            Substitute.For<IHostDevelopmentCertificate>();
        public TestConsole Console { get; } = new();
        public SandboxHttps Https { get; }

        public void Activation(SandboxCertificateActivation activation) =>
            Certificates
                .ActivateAsync(
                    Arg.Any<string>(),
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(activation);
    }
}
