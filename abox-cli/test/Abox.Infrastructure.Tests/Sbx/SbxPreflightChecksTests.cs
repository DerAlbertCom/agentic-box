using Abox.Features.Shared.Common;
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
        _checks = new SbxPreflightChecks(new SbxInvoker(_runner), _programs);
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
    [InlineData("v0.54.0", true)]
    [InlineData("0.55.1", true)]
    [InlineData("v0.53.9", false)]
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
                        "version": "v0.54.0",
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
        result.Minimum.Should().Be(new Version(0, 54, 0));
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
            .Equal(
                new DiagnosticCheck("Daemon", true, "not healthy", "no answer", "start the daemon")
            );
        report
            .Checks.Select(check => check.Name)
            .Should()
            .Equal("Authentication", "Daemon", "Network");
    }

    [Fact]
    public async Task AFailedCheckMakesDiagnoseExitNonZeroButItsReportIsStillRead()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    1,
                    """
                    {
                      "version": "1.1",
                      "checks": [
                        {
                          "id": "binary-version",
                          "name": "Binary version",
                          "status": "warn",
                          "message": "update available",
                          "detail": "https://example.invalid/release",
                          "hint": "Download the latest release"
                        },
                        {
                          "id": "daemon",
                          "name": "Daemon",
                          "status": "fail",
                          "message": "not reachable",
                          "detail": "health endpoint: connection refused",
                          "hint": "Run: sbx daemon start"
                        },
                        {
                          "id": "authentication",
                          "name": "Authentication",
                          "status": "skip",
                          "message": "Skipped: Daemon failed",
                          "detail": "",
                          "hint": ""
                        }
                      ],
                      "summary": { "pass": 0, "warn": 1, "fail": 1, "skip": 1 }
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
            .Equal(
                new DiagnosticCheck(
                    "Daemon",
                    true,
                    "not reachable",
                    "health endpoint: connection refused",
                    "Run: sbx daemon start"
                )
            );
    }

    [Fact]
    public async Task ACheckSbxTimedOutIsReadAsTimedOutAndNotAsAFailure()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    1,
                    """
                    {
                      "version": "1.1",
                      "checks": [
                        {
                          "id": "daemon-diagnostics",
                          "name": "Daemon diagnostics",
                          "status": "fail",
                          "message": "check timed out",
                          "detail": "This check did not complete within 5s",
                          "hint": ""
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
        report.Failures.Should().BeEmpty();
        report.TimedOut.Should().ContainSingle().Which.Name.Should().Be("Daemon diagnostics");
    }

    [Fact]
    public async Task ANonZeroExitWithoutAReportIsAnErrorThatNamesTheCommand()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(2, string.Empty, "unknown flag: --json"));

        // Act
        var action = () => _checks.DiagnoseAsync(CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "'sbx diagnose' failed: unknown flag: --json"
        );
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
