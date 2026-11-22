using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxHttpsTests
{
    [Fact]
    public async Task LaunchReadinessFailureRefusesWithRecoveryGuidance()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var exec = Substitute.For<ISandboxExec>();
        exec.RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxExecOutcome(78));
        var https = Create(exec, root.Path);

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
        using var root = new CertificateTestDirectory();
        var exec = SandboxFactory.Exec();
        var https = Create(exec, root.Path);

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

    private static SandboxHttps Create(ISandboxExec exec, string root)
    {
        var console = new TestConsole();
        return new SandboxHttps(
            new HostDevelopmentCertificate(Substitute.For<IProcessRunner>(), console, root, []),
            Substitute.For<ISbxClient>(),
            exec,
            console
        );
    }
}
