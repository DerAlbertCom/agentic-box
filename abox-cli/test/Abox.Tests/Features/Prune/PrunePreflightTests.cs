using System.CommandLine;
using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.SandboxCleanup;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class PrunePreflightTests
{
    [Fact]
    public async Task RecoveredAuthenticationAllowsStopsAndRemovalOnlyAfterRediagnosis()
    {
        // Arrange
        using var cli = new PruneCli();

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(0);
        cli.Scenario.Console.Errors.Should().BeEmpty();
        cli.Scenario.Console.Output.Should().Contain("Removed sbx-kit-src:old.");
        cli.Scenario.Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
        cli.CleanupCalls.Should()
            .Equal(
                "ListSandboxesAsync",
                "StopSandboxAsync",
                "ListTemplatesAsync",
                "RemoveTemplateAsync"
            );
        Received.InOrder(() =>
        {
            _ = cli.Scenario.Checks.DiagnoseAsync(Arg.Any<CancellationToken>());
            _ = cli.Scenario.Checks.DiagnoseAsync(Arg.Any<CancellationToken>());
            cli.Cleanup.ListSandboxesAsync(Arg.Any<CancellationToken>());
        });
        await cli
            .Cleanup.Received(1)
            .StopSandboxAsync("synthetic-running", Arg.Any<CancellationToken>());
        await cli
            .Cleanup.Received(1)
            .RemoveTemplateAsync(
                Arg.Is<TemplateImage>(image => image.Reference == "sbx-kit-src:old"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task DeclinedOrFailedSignInPreventsAllPruneOperations()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Checks.SignInAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Errors.Should()
            .Equal("Signing in failed. Run 'sbx login' and try again.");
        cli.Scenario.Console.AllText.Should().NotContain("credential-sentinel");
        cli.Scenario.Calls.Should()
            .Equal("IsInstalled", "CheckVersionAsync", "DiagnoseAsync", "SignInAsync");
        cli.CleanupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task NoninteractiveAuthenticationFailureCannotStopSandboxesOrRemoveImages()
    {
        // Arrange
        using var cli = new PruneCli(interactive: false);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Output.Should().BeEmpty();
        cli.Scenario.Console.Errors.Should()
            .Equal("sbx is not signed in. Run 'sbx login' and try again.");
        cli.Scenario.Calls.Should().Equal("IsInstalled", "CheckVersionAsync", "DiagnoseAsync");
        cli.CleanupCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Authentication", "Run 'sbx login' and try again.")]
    [InlineData("Daemon", "Start the Docker Sandboxes daemon and try again.")]
    public async Task ASecondUnhealthyDiagnosisPreventsAllPruneOperations(string check, string hint)
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Checks.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                PreflightScenario.SignedOut(),
                new DiagnoseReport(
                    [new DiagnosticCheck(check, true, "not ready", hint)],
                    SignedOut: check == "Authentication"
                )
            );

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Errors.Should().Equal($"sbx check '{check}' failed: not ready", hint);
        cli.Scenario.Console.AllText.Should().NotContain("credential-sentinel");
        cli.Scenario.Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
        cli.CleanupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedRediagnosisIsAnErrorRatherThanPermissionToPrune()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Checks.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(PreflightScenario.SignedOut()),
                _ =>
                    throw new AboxException(
                        "Could not diagnose sbx. Check the daemon and try again."
                    )
            );

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.Errors.Should()
            .Equal("error: Could not diagnose sbx. Check the daemon and try again.");
        cli.Scenario.Console.AllText.Should().NotContain("credential-sentinel");
        cli.Scenario.Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
        cli.CleanupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingSbxStopsBeforeVersionDiagnosisLoginOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Checks.IsInstalled().Returns(false);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.AllText.Should()
            .Contain("not on PATH")
            .And.Contain("never installs anything itself");
        cli.Scenario.Calls.Should().Equal("IsInstalled");
        cli.CleanupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task OldSbxStopsBeforeDiagnosisLoginOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        cli.Scenario.Checks.CheckVersionAsync(Arg.Any<CancellationToken>())
            .Returns(ReadyPreflight.OldVersion);

        // Act
        var exitCode = await cli.Root.InvokeAsync(["prune", "--full", "--sbx"]);

        // Assert
        exitCode.Should().Be(1);
        cli.Scenario.Console.AllText.Should()
            .Contain("too old")
            .And.Contain("Upgrade sbx and try again.");
        cli.Scenario.Calls.Should().Equal("IsInstalled", "CheckVersionAsync");
        cli.CleanupCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationDuringLoginReturns130WithoutRediagnosisOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        cli.Scenario.Checks.SignInAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<bool>(call.Arg<CancellationToken>());
            });

        // Act
        var exitCode = await cli
            .Root.Parse(["prune", "--full", "--sbx"])
            .InvokeAsync(cancellationToken: token);

        // Assert
        exitCode.Should().Be(130);
        cli.Scenario.Console.Errors.Should().Equal("error: the command was cancelled.");
        cli.Scenario.Calls.Should()
            .Equal("IsInstalled", "CheckVersionAsync", "DiagnoseAsync", "SignInAsync");
        cli.CleanupCalls.Should().BeEmpty();
        // System.CommandLine links the invocation token; use the actual token seen at version checking.
        cli.Scenario.CommandToken.IsCancellationRequested.Should().BeTrue();
        await cli.Scenario.Checks.Received(1).SignInAsync(cli.Scenario.CommandToken);
    }

    [Fact]
    public async Task CancellationDuringRediagnosisReturns130WithoutRetryOrPruning()
    {
        // Arrange
        using var cli = new PruneCli();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        cli.Scenario.Checks.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(PreflightScenario.SignedOut()),
                call =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<DiagnoseReport>(call.Arg<CancellationToken>());
                }
            );

        // Act
        var exitCode = await cli
            .Root.Parse(["prune", "--full", "--sbx"])
            .InvokeAsync(cancellationToken: token);

        // Assert
        exitCode.Should().Be(130);
        cli.Scenario.Console.Errors.Should().Equal("error: the command was cancelled.");
        cli.Scenario.Calls.Should()
            .Equal(
                "IsInstalled",
                "CheckVersionAsync",
                "DiagnoseAsync",
                "SignInAsync",
                "DiagnoseAsync"
            );
        cli.CleanupCalls.Should().BeEmpty();
        cli.Scenario.CommandToken.IsCancellationRequested.Should().BeTrue();
        await cli.Scenario.Checks.Received(2).DiagnoseAsync(cli.Scenario.CommandToken);
    }

    private sealed class PruneCli : IDisposable
    {
        private readonly TempDirectory _directory = new();
        private readonly CliTestApplication _application;

        public PruneCli(bool interactive = true)
        {
            Scenario = new PreflightScenario(interactive);
            SeedPrunableRuntime();
            _application = new CliTestApplication(
                Scenario.Console,
                AboxPaths.For(_directory.Path, Path.Combine(_directory.Path, "state")),
                new TrailingArguments()
            );
            var services = _application.Services;
            services.AddSingleton(Scenario.Checks);
            services.AddSingleton(Cleanup);
            _ = _application.Provider;
        }

        // A bypass must reach real stop/removal targets, not pass on an empty inventory.
        private void SeedPrunableRuntime()
        {
            Cleanup
                .ListSandboxesAsync(Arg.Any<CancellationToken>())
                .Returns(new[] { new SandboxSummary("synthetic-running", true) });
            Cleanup.StopSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
            Cleanup
                .ListTemplatesAsync(Arg.Any<CancellationToken>())
                .Returns(
                    new[]
                    {
                        new TemplateImage(
                            "sbx-kit-src:old",
                            TemplateKind.KitSource,
                            string.Empty,
                            "synthetic-id",
                            DateTimeOffset.Parse("2026-11-01T00:00:00Z")
                        ),
                    }
                );
            Cleanup
                .RemoveTemplateAsync(Arg.Any<TemplateImage>(), Arg.Any<CancellationToken>())
                .Returns(TemplateRemoval.Done);
        }

        public ISandboxCleanup Cleanup { get; } = Substitute.For<ISandboxCleanup>();

        public IEnumerable<string> CleanupCalls =>
            Cleanup.ReceivedCalls().Select(call => call.GetMethodInfo().Name);

        public PreflightScenario Scenario { get; }
        public AboxRootCommand Root => _application.Provider.GetRequiredService<AboxRootCommand>();

        public void Dispose()
        {
            _application.Dispose();
            _directory.Dispose();
        }
    }
}
