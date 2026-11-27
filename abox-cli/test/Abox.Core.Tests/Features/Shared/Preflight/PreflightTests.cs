using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;
using NSubstitute;

namespace Abox.Tests;

public sealed class PreflightTests
{
    private readonly TestConsole _console = new();
    private readonly IPreflightChecks _checks = ReadyPreflight.Checks();

    [Fact]
    public async Task WithoutSbxTheInstallStepsArePrinted()
    {
        // Arrange
        _checks.IsInstalled().Returns(false);
        var preflight = new Preflight(_console, _checks);

        // Act
        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        _console
            .Errors.Should()
            .StartWith("sbx is not on PATH. abox needs it and never installs anything itself.")
            .And.ContainInOrder(InstallInstructions.ForCurrentSystem());
        _checks
            .ReceivedCalls()
            .Select(call => call.GetMethodInfo().Name)
            .Should()
            .Equal("IsInstalled");
    }

    [Fact]
    public async Task AnOldSbxIsRefused()
    {
        // Arrange
        _checks.CheckVersionAsync(Arg.Any<CancellationToken>()).Returns(ReadyPreflight.OldVersion);
        var preflight = new Preflight(_console, _checks);

        // Act
        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        _console
            .Errors.Should()
            .Equal(
                "sbx 0.53.0 is too old. abox needs 0.54.0 or later.",
                "Upgrade sbx and try again."
            );
        await _checks.DidNotReceive().DiagnoseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnreadableVersionStopsTheCommandWithItsMessage()
    {
        // Arrange
        _checks
            .CheckVersionAsync(Arg.Any<CancellationToken>())
            .Returns<RuntimeVersion>(_ =>
                throw new AboxException("Cannot read the sbx version from ''.")
            );
        var preflight = new Preflight(_console, _checks);

        // Act
        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        _console.Errors.Should().Equal("Cannot read the sbx version from ''.");
        await _checks.DidNotReceive().DiagnoseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailingCheckStopsTheCommand()
    {
        // Arrange
        _checks
            .DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                new DiagnoseReport(
                    [
                        new DiagnosticCheck("Authentication", false, "authenticated", "", ""),
                        new DiagnosticCheck(
                            "Daemon",
                            true,
                            "not reachable",
                            "health endpoint: connection refused",
                            "Run: sbx daemon start"
                        ),
                    ],
                    SignedOut: false
                )
            );
        var preflight = new Preflight(_console, _checks);

        // Act
        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        _console
            .Errors.Should()
            .Equal(
                "sbx check 'Daemon' failed: not reachable",
                "health endpoint: connection refused",
                "Run: sbx daemon start"
            );
        await _checks.DidNotReceive().SignInAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACheckThatTimedOutIsAWarningAndTheCommandGoesOn()
    {
        // Arrange
        _checks
            .DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                new DiagnoseReport(
                    [
                        new DiagnosticCheck("Daemon", false, "healthy", "", ""),
                        new DiagnosticCheck(
                            "Daemon diagnostics",
                            true,
                            "check timed out",
                            "This check did not complete within 5s",
                            "",
                            TimedOut: true
                        ),
                    ],
                    SignedOut: false
                )
            );
        var preflight = new Preflight(_console, _checks);

        // Act
        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeTrue();
        _console
            .Errors.Should()
            .Equal(
                "Warning: sbx check 'Daemon diagnostics' did not finish: check timed out",
                "This check did not complete within 5s"
            );
    }

    [Fact]
    public async Task AHealthySbxLetsTheCommandGoOn()
    {
        // Arrange
        var preflight = new Preflight(_console, _checks);

        // Act
        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeTrue();
        _console.AllText.Should().BeEmpty();
        await _checks.DidNotReceive().SignInAsync(Arg.Any<CancellationToken>());
    }
}
