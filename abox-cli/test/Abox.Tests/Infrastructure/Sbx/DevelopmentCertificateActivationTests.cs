using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class DevelopmentCertificateActivationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActivationTrustsOnlyTheExactPublicLeafAsRootBeforeNonrootPrivateActivation(
        bool trusted
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        var password = new string('a', 64);
        var pfx = certificate.Export(X509ContentType.Pfx, password);
        var calls = new List<ProcessRequest>();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var process = call.Arg<ProcessRequest>();
                calls.Add(process);
                return new ProcessResult(
                    process.Arguments.Contains("root") && !trusted ? 1 : 0,
                    process.Arguments.Contains("id") ? "1000\n" : "",
                    "synthetic private child output"
                );
            });
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var activation = await client.ActivateAsync(
            "actual",
            pfx,
            password,
            CancellationToken.None
        );

        // Assert
        activation
            .Status.Should()
            .Be(
                trusted
                    ? SandboxCertificateActivationStatus.Activated
                    : SandboxCertificateActivationStatus.TrustFailed
            );
        var rootCall = calls.Single(process => process.Arguments.Contains("root"));
        rootCall.StandardInput.Should().Be(certificate.ExportCertificatePem());
        rootCall.StandardInput.Should().NotContain(password).And.NotContain("PRIVATE KEY");
        rootCall.Arguments.Should().Contain("1000");
        rootCall.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        calls.Any(process => process.Arguments.Contains("activate")).Should().Be(trusted);
        if (trusted)
            calls
                .IndexOf(rootCall)
                .Should()
                .BeLessThan(calls.FindIndex(process => process.Arguments.Contains("activate")));
    }

    [Fact]
    public async Task StaleExistingParentsAreReportedAndNeverSilentlyRestarted()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(42, "private", "private"));
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var status = await client.VerifyActivationAsync("actual", CancellationToken.None);

        // Assert
        status.Should().Be(SandboxCertificateStatus.StaleParents);
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.OutputPolicy == ProcessOutputPolicy.Sensitive
                    && !request.Arguments.Contains("restart")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(0, SandboxCertificateStatus.Succeeded)]
    [InlineData(43, SandboxCertificateStatus.ProcessEnvironmentsUnavailable)]
    [InlineData(44, SandboxCertificateStatus.NodeExtraCaIncomplete)]
    [InlineData(75, SandboxCertificateStatus.Busy)]
    [InlineData(17, SandboxCertificateStatus.Failed)]
    public async Task ReadinessExitCodesBecomeTheirReadinessOutcome(
        int code,
        SandboxCertificateStatus expected
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(code, "private child stdout", "private child stderr"));
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var status = await client.VerifyActivationAsync("actual", CancellationToken.None);

        // Assert
        status.Should().Be(expected);
        var observed = (ProcessRequest)
            runner.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0]!;
        observed.Arguments.Should().Contain("status").And.NotContain("root").And.NotContain("-i");
        observed.StandardInput.Should().BeEmpty();
        observed.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        observed.Environment.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000\n2000")]
    [InlineData("1e3")]
    public async Task InvalidApplicationIdentityStopsBeforeRootTrustOrPrivateActivation(string uid)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var pfx = SyntheticCertificatePayload();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, uid, "synthetic private identity"));
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var activation = await client.ActivateAsync(
            "actual",
            pfx,
            "password",
            CancellationToken.None
        );

        // Assert
        activation.Status.Should().Be(SandboxCertificateActivationStatus.TrustFailed);
        var observed = (ProcessRequest)
            runner.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0]!;
        observed.Arguments.Should().Equal("exec", "actual", "id", "-u");
        observed.OutputPolicy.Should().Be(ProcessOutputPolicy.CapturedSensitive);
    }

    [Fact]
    public async Task AnUntransferablePasswordIsReportedAfterTrustWithoutAPrivateTransfer()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var calls = new List<ProcessRequest>();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var process = call.Arg<ProcessRequest>();
                calls.Add(process);
                return new ProcessResult(0, process.Arguments.Contains("id") ? "1000\n" : "", "");
            });
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var activation = await client.ActivateAsync(
            "actual",
            SyntheticCertificatePayload(""),
            "",
            CancellationToken.None
        );

        // Assert
        activation.Status.Should().Be(SandboxCertificateActivationStatus.InvalidPayload);
        calls.Should().NotContain(process => process.Arguments.Contains("activate"));
    }

    private static byte[] SyntheticCertificatePayload(string password = "password")
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        return certificate.Export(X509ContentType.Pfx, password);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("activate")]
    [InlineData("refresh-bootstrap")]
    public async Task RuntimeLockTimeoutIsReportedAsBusy(string mode)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var arguments = call.Arg<ProcessRequest>().Arguments;
                if (arguments.Contains("id"))
                    return new ProcessResult(0, "1000\n", "");
                return new ProcessResult(
                    arguments.Contains("cleanup") || arguments.Contains("root") ? 0 : 75,
                    "private child stdout",
                    "private child stderr"
                );
            });
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var busy = mode switch
        {
            "prepare" => (await client.PrepareAsync("actual", CancellationToken.None)).Status
                == SandboxCertificateStatus.Busy,
            "refresh-bootstrap" => await client.RefreshBootstrapAsync(
                "actual",
                CancellationToken.None
            ) == SandboxCertificateStatus.Busy,
            _ => (
                await client.ActivateAsync(
                    "actual",
                    SyntheticCertificatePayload(),
                    "password",
                    CancellationToken.None
                )
            ) == new SandboxCertificateActivation(SandboxCertificateActivationStatus.TransferBusy),
        };

        // Assert
        busy.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapRefreshThatCannotRunOrIsCanceledIsReportedAsSuch(bool cancel)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ProcessResult>(
                    cancel
                        ? new OperationCanceledException("private child diagnostics")
                        : new AboxException("private child diagnostics")
                )
            );
        var client = new SbxDevelopmentCertificates(new SbxInvoker(runner));

        // Act
        var status = await client.RefreshBootstrapAsync("actual", CancellationToken.None);

        // Assert
        status
            .Should()
            .Be(cancel ? SandboxCertificateStatus.Canceled : SandboxCertificateStatus.Unavailable);
    }
}
