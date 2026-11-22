using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Sandboxes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class ExecPipeCommandTests
{
    [Theory]
    [InlineData("exec")]
    [InlineData("pipe")]
    public async Task WithoutACommandAfterTheSeparatorNothingRuns(string command)
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var exec = Substitute.For<ISandboxExec>();
        using var application = new CliTestApplication(
            console,
            AboxPaths.For(root.Path, Path.Combine(root.Path, "state")),
            new TrailingArguments()
        );
        application.Services.AddSingleton(exec);

        // Act
        var exitCode = await application
            .Provider.GetRequiredService<AboxRootCommand>()
            .InvokeAsync([command, "sample"]);

        // Assert
        exitCode.Should().Be(1);
        console
            .Errors.Should()
            .Equal($"error: no command given. Use: abox {command} <sandbox> -- <command>");
        exec.ReceivedCalls().Should().BeEmpty();
    }
}
