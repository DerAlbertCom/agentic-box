using Abox.Features.Shared.Preflight;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxPreflightChecksTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IHostPrograms _programs = Substitute.For<IHostPrograms>();
    private readonly SbxPreflightChecks _checks;

    public SbxPreflightChecksTests()
    {
        _checks = new SbxPreflightChecks(_runner, _programs);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SbxIsInstalledWhenItIsOnThePath(bool onPath)
    {
        // Arrange
        _programs.Exists("sbx").Returns(onPath);

        // Act
        var installed = _checks.IsInstalled();

        // Assert
        installed.Should().Be(onPath);
    }

    [Theory]
    [InlineData("v0.51.0", true)]
    [InlineData("0.52.1", true)]
    [InlineData("v0.50.9", false)]
    public async Task TheVersionIsReadFromTheJsonOutputAndComparedWithTheMinimum(
        string version,
        bool supported
    )
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    $$"""
                    {
                      "client": {
                        "version": "{{version}}",
                        "revision": "7d55286de0c775148b985ed349f760f1eba06203"
                      },
                      "server": {
                        "state": "running",
                        "version": "v0.51.0",
                        "revision": "7d55286de0c775148b985ed349f760f1eba06203",
                        "api_version": "0.49.0"
                      }
                    }
                    """,
                    string.Empty
                )
            );

        // Act
        var result = await _checks.CheckVersionAsync(CancellationToken.None);

        // Assert
        result.Installed.Should().Be(Version.Parse(version.TrimStart('v')));
        result.Minimum.Should().Be(new Version(0, 51, 0));
        result.Supported.Should().Be(supported);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(new[] { "version", "--json" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AVersonlessAnswerIsAnError()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "{\"server\": {\"state\": \"stopped\"}}", string.Empty));

        // Act
        var call = async () => await _checks.CheckVersionAsync(CancellationToken.None);

        // Assert
        (await call.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Cannot read the sbx version from ''*"
        );
    }

    [Fact]
    public async Task TheDiagnoseReportIsReadFromTheJsonOutput()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    {
                      "version": "1.0",
                      "checks": [
                        {
                          "name": "Authentication",
                          "status": "pass",
                          "message": "authenticated"
                        },
                        {
                          "name": "Daemon",
                          "status": "fail",
                          "message": "not healthy",
                          "detail": "no answer",
                          "hint": "start the daemon"
                        },
                        {
                          "name": "Network",
                          "status": "skip",
                          "message": "skipped"
                        }
                      ]
                    }
                    """,
                    string.Empty
                )
            );

        // Act
        var report = await _checks.DiagnoseAsync(CancellationToken.None);

        // Assert
        report.SignedOut.Should().BeFalse();
        report
            .Failures.Should()
            .Equal(new DiagnosticCheck("Daemon", true, "not healthy", "start the daemon"));
        report
            .Checks.Select(check => check.Name)
            .Should()
            .Equal("Authentication", "Daemon", "Network");
    }

    [Theory]
    [InlineData("Authentication", "fail", true)]
    [InlineData("authentication", "FAIL", true)]
    [InlineData("Authentication", "pass", false)]
    [InlineData("Daemon", "fail", false)]
    public async Task AFailedAuthenticationCheckMeansSignedOut(
        string name,
        string status,
        bool signedOut
    )
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    $$"""{"checks":[{"name":"{{name}}","status":"{{status}}","message":"m"}]}""",
                    string.Empty
                )
            );

        // Act
        var report = await _checks.DiagnoseAsync(CancellationToken.None);

        // Assert
        report.SignedOut.Should().Be(signedOut);
    }

    [Fact]
    public async Task EmptyOutputIsAnErrorThatNamesTheCommand()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        // Act
        var action = () => _checks.DiagnoseAsync(CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("sbx diagnose");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task SignInRunsTheInteractiveLoginOnTheConsole(int exitCode, bool signedIn)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, string.Empty, string.Empty));

        // Act
        var result = await _checks.SignInAsync(CancellationToken.None);

        // Assert
        result.Should().Be(signedIn);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(new[] { "login" })
                    && request.AttachConsole
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
