using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Infrastructure;
using Abox.Infrastructure.Shell;

namespace Abox.Tests;

/// <summary>The value command of a secret, run with a real POSIX shell on the host.</summary>
public sealed class ShellHostCommandsTests
{
    private readonly TestConsole _console = new();

    [Fact]
    public async Task TheOutputOfTheCommandIsReturnedAndNeverShownEvenWhenVerbose()
    {
        // Arrange
        var commands = new ShellHostCommands(
            new ProcessRunner(new ProcessOutput(_console, new Verbosity { Verbose = true }))
        );

        // Act
        var result = await commands.RunAsync(
            "printf 'synthetic-%s\\n' value; echo warning >&2",
            default
        );

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Output.Trim().Should().Be("synthetic-value");
        _console.AllText.Should().NotContain("synthetic-value").And.NotContain("warning");
    }

    [Fact]
    public async Task AFailingCommandGivesItsExitCode()
    {
        // Arrange
        var commands = new ShellHostCommands(new ProcessRunner());

        // Act
        var result = await commands.RunAsync("exit 3", default);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(3);
    }

    [Fact]
    public async Task OnWindowsWithoutAShellTheCommandFailsNamingGitForWindows()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "Only Windows runs the command with the sh of Git."
            );
        }

        var commands = new ShellHostCommands(new ProcessRunner(), () => null);

        // Act
        var run = () => commands.RunAsync("gh auth token", default);

        // Assert
        (await run.Should().ThrowAsync<AboxException>()).WithMessage(
            "The host command 'gh auth token' of a secret runs on Windows with the sh of Git for Windows*"
        );
    }
}
