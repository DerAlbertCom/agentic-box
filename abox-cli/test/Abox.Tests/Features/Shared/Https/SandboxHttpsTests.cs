using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxHttpsTests
{
    [Fact]
    public async Task LaunchReadinessFailureRefusesWithRecoveryGuidance()
    {
        // Arrange
        var exec = Substitute.For<ISandboxExec>();
        exec.RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxExecOutcome(78));
        var https = Create(exec);

        // Act
        var action = () => https.EnsureLaunchReadyAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be(
                "Herdr was not opened: sandbox HTTPS launch readiness failed. Exit stale agents and explicitly initialize fresh core/service parents through abox after saving work; rerun 'abox ports' for recovery guidance. No core or app was restarted."
            );
    }

    [Fact]
    public async Task LaunchReadinessRunsANoOpCommandInTheSandbox()
    {
        // Arrange
        var exec = SandboxFactory.Exec();
        var https = Create(exec);

        // Act
        await https.EnsureLaunchReadyAsync("actual", CancellationToken.None);

        // Assert
        await exec.Received(1)
            .RunAsync(
                Arg.Is<SandboxExecRequest>(request =>
                    request.Sandbox == "actual"
                    && request.Command.SequenceEqual(new[] { "true" })
                    && request.Input == SandboxExecInput.None
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ConfigurationPreparesExportsActivatesAndVerifiesInOrder()
    {
        // Arrange
        var certificates = SandboxFactory.Certificates();
        certificates
            .PrepareAsync("actual", Arg.Any<CancellationToken>())
            .Returns(
                new SandboxCertificatePreparation(
                    SandboxCertificateStatus.Succeeded,
                    "runtime-password"
                )
            );
        var exporter = Substitute.For<IHostDevelopmentCertificate>();
        exporter
            .ExportAsync("runtime-password", Arg.Any<CancellationToken>())
            .Returns(_ => new ExportedDevelopmentCertificate([1, 2, 3], "runtime-password"));
        byte[]? activated = null;
        certificates
            .When(c =>
                c.ActivateAsync(
                    "actual",
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    "runtime-password",
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call => activated = call.Arg<ReadOnlyMemory<byte>>().ToArray());
        var console = new TestConsole();
        var https = new SandboxHttps(exporter, certificates, SandboxFactory.Exec(), console);

        // Act
        await https.ConfigureAsync("actual", CancellationToken.None);

        // Assert
        Received.InOrder(() =>
        {
            certificates.PrepareAsync("actual", Arg.Any<CancellationToken>());
            exporter.ExportAsync("runtime-password", Arg.Any<CancellationToken>());
            certificates.ActivateAsync(
                "actual",
                Arg.Any<ReadOnlyMemory<byte>>(),
                "runtime-password",
                Arg.Any<CancellationToken>()
            );
            certificates.VerifyActivationAsync("actual", Arg.Any<CancellationToken>());
        });
        activated.Should().Equal(1, 2, 3);
        console
            .Output.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("HTTPS default certificate");
    }

    [Fact]
    public async Task ActivationFailureStopsBeforeVerificationAndReportsNothing()
    {
        // Arrange
        var certificates = SandboxFactory.Certificates();
        certificates
            .ActivateAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new SandboxCertificateActivation(SandboxCertificateActivationStatus.TrustFailed)
            );
        var exporter = Substitute.For<IHostDevelopmentCertificate>();
        exporter
            .ExportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ExportedDevelopmentCertificate([1], "password"));
        var console = new TestConsole();
        var https = new SandboxHttps(exporter, certificates, SandboxFactory.Exec(), console);

        // Act
        var action = () => https.ConfigureAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .StartWith("Cannot establish exact public development-certificate sandbox trust.");
        await certificates
            .DidNotReceive()
            .VerifyActivationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        console.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task BootstrapRefreshNeitherExportsNorActivates()
    {
        // Arrange
        var certificates = SandboxFactory.Certificates();
        var exporter = Substitute.For<IHostDevelopmentCertificate>();
        var console = new TestConsole();
        var https = new SandboxHttps(exporter, certificates, SandboxFactory.Exec(), console);

        // Act
        await https.RefreshBootstrapAsync("actual", CancellationToken.None);

        // Assert
        await certificates
            .Received(1)
            .RefreshBootstrapAsync("actual", Arg.Any<CancellationToken>());
        exporter.ReceivedCalls().Should().BeEmpty();
        console
            .Output.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("HTTPS bootstrap refreshed");
    }

    private static SandboxHttps Create(ISandboxExec exec)
    {
        var console = new TestConsole();
        return new SandboxHttps(
            Substitute.For<IHostDevelopmentCertificate>(),
            SandboxFactory.Certificates(),
            exec,
            console
        );
    }
}
