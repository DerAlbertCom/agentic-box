using Abox.Features.Shared.Preflight;
using NSubstitute;

namespace Abox.Tests;

public sealed class AuthenticationPreflightTests
{
    [Fact]
    public async Task InteractiveSignInRequiresASuccessfulSecondDiagnosis()
    {
        // Arrange
        var scenario = new PreflightScenario();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        // Act
        var ready = await scenario.Preflight.EnsureReadyAsync(token);

        // Assert
        ready.Should().BeTrue();
        scenario.Console.Output.Should().Equal("Signing in to Docker...");
        scenario.Console.Errors.Should().BeEmpty();
        scenario
            .Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
        Received.InOrder(() =>
        {
            scenario.Checks.IsInstalled();
            _ = scenario.Checks.CheckVersionAsync(token);
            _ = scenario.Checks.DiagnoseAsync(token);
            _ = scenario.Checks.SignInAsync(token);
            _ = scenario.Checks.DiagnoseAsync(token);
        });
    }

    [Fact]
    public async Task DeclinedOrFailedSignInGivesASafeRetryWithoutRediagnosis()
    {
        // Arrange
        var scenario = new PreflightScenario();
        scenario.Checks.SignInAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var ready = await scenario.Preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        scenario.Console.Output.Should().Equal("Signing in to Docker...");
        scenario.Console.Errors.Should().Equal("Signing in failed. Run 'sbx login' and try again.");
        scenario.Console.AllText.Should().NotContain("credential-sentinel");
        scenario
            .Calls.Should()
            .Equal("IsInstalled", "CheckVersionAsync", "DiagnoseAsync", "SignInAsync");
    }

    [Fact]
    public async Task NoninteractiveAuthenticationFailureRequiresManualLoginWithoutOpeningABrowser()
    {
        // Arrange
        var scenario = new PreflightScenario(interactive: false);

        // Act
        var ready = await scenario.Preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        scenario.Console.Output.Should().BeEmpty();
        scenario
            .Console.Errors.Should()
            .Equal("sbx is not signed in. Run 'sbx login' and try again.");
        scenario.Console.AllText.Should().NotContain("credential-sentinel");
        scenario.Calls.Should().Equal("IsInstalled", "CheckVersionAsync", "DiagnoseAsync");
    }

    [Theory]
    [InlineData("Authentication", "still signed out", "Run 'sbx login' and try again.")]
    [InlineData("Daemon", "not healthy", "Start the Docker Sandboxes daemon and try again.")]
    public async Task SuccessfulLoginDoesNotOverrideAFailingSecondDiagnosis(
        string check,
        string message,
        string hint
    )
    {
        // Arrange
        var scenario = new PreflightScenario();
        scenario
            .Checks.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                PreflightScenario.SignedOut(),
                new DiagnoseReport(
                    [new DiagnosticCheck(check, true, message, hint)],
                    SignedOut: check == "Authentication"
                )
            );

        // Act
        var ready = await scenario.Preflight.EnsureReadyAsync(CancellationToken.None);

        // Assert
        ready.Should().BeFalse();
        scenario.Console.Errors.Should().Equal($"sbx check '{check}' failed: {message}", hint);
        scenario.Console.AllText.Should().NotContain("credential-sentinel");
        scenario
            .Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
    }

    [Fact]
    public async Task CancellationDuringLoginPropagatesTheCallerTokenWithoutRediagnosis()
    {
        // Arrange
        var scenario = new PreflightScenario();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        scenario
            .Checks.SignInAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<bool>(call.Arg<CancellationToken>());
            });

        // Act
        var act = () => scenario.Preflight.EnsureReadyAsync(token);

        // Assert
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(token);
        scenario.Console.Errors.Should().BeEmpty();
        scenario
            .Calls.Should()
            .Equal("IsInstalled", "CheckVersionAsync", "DiagnoseAsync", "SignInAsync");
        await scenario.Checks.Received(1).SignInAsync(token);
    }

    [Fact]
    public async Task CancellationDuringRediagnosisPropagatesTheCallerTokenWithoutRetrying()
    {
        // Arrange
        var scenario = new PreflightScenario();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        scenario
            .Checks.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(PreflightScenario.SignedOut()),
                call =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<DiagnoseReport>(call.Arg<CancellationToken>());
                }
            );

        // Act
        var act = () => scenario.Preflight.EnsureReadyAsync(token);

        // Assert
        var exception = await act.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(token);
        scenario.Console.Errors.Should().BeEmpty();
        scenario
            .Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
        await scenario.Checks.Received(2).DiagnoseAsync(token);
    }
}
