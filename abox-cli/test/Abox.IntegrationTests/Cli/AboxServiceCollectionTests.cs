using System.CommandLine;
using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AboxServiceCollectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCompositionRootWiresVerbosityThroughToExternalTools(bool verbose)
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var paths = AboxPaths.For(root.Path, root.Path);
        using var application = new CliTestApplication(console, paths, new TrailingArguments());
        application.Services.AddScoped<Command>(provider =>
        {
            var command = new Command("test-tool");
            command.SetAction(
                async (_, cancellationToken) =>
                {
                    var result = await provider
                        .GetRequiredService<IProcessRunner>()
                        .RunAsync(
                            new ProcessRequest(
                                OperatingSystem.IsWindows() ? "cmd" : "sh",
                                OperatingSystem.IsWindows()
                                    ? ["/c", "echo tool-output & echo tool-error >&2 & exit /b 19"]
                                    : ["-c", "echo tool-output; echo tool-error >&2; exit 19"]
                            ),
                            cancellationToken
                        );
                    return result.ExitCode;
                }
            );
            return command;
        });
        var provider = application.Provider;

        // Act
        var exitCode = await provider
            .GetRequiredService<AboxRootCommand>()
            .InvokeAsync(verbose ? ["test-tool", "--verbose"] : ["test-tool"]);

        // Assert
        exitCode.Should().Be(19);
        console.Output.Should().BeEmpty();
        if (verbose)
        {
            console.AllText.Should().Contain("tool-output").And.Contain("tool-error");
        }
        else
        {
            console.AllText.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("build", "--help")]
    [InlineData("secrets", "list", "--help")]
    [InlineData("user-secrets", "--help")]
    [InlineData("ports", "--help")]
    [InlineData("exec", "sample")]
    [InlineData("pipe", "sample")]
    public async Task CommandsThatDoNotUseConfigurationWorkEvenWhenTheFileIsInvalid(
        params string[] arguments
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("config.yaml", "unknown-key: invalid\n");
        var console = new TestConsole();
        var exec = Substitute.For<ISandboxExec>();
        exec.RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxExecOutcome(0));
        using var application = new CliTestApplication(
            console,
            AboxPaths.For(root.Path, Path.Combine(root.Path, "state")),
            new TrailingArguments { Values = ["echo", "hello"] }
        );
        application.Services.AddSingleton(exec);
        var provider = application.Provider;

        // Act
        var exitCode = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync(arguments);

        // Assert
        exitCode.Should().Be(0);
        console.Errors.Should().BeEmpty();
        Directory.Exists(Path.Combine(root.Path, "state")).Should().BeFalse();
        if (arguments[0] is "exec" or "pipe")
        {
            await exec.Received(1)
                .RunAsync(
                    Arg.Is<SandboxExecRequest>(request =>
                        request.Sandbox == "sample"
                        && request.Command.SequenceEqual(new[] { "echo", "hello" })
                        && request.Input
                            == (
                                arguments[0] == "pipe"
                                    ? SandboxExecInput.Forwarded
                                    : SandboxExecInput.None
                            )
                    ),
                    Arg.Any<CancellationToken>()
                );
        }
    }

    [Theory]
    [InlineData("build")]
    [InlineData("create")]
    [InlineData("publish")]
    public async Task ConfigurationErrorsAreReportedOnceWhenACommandUsesIt(string command)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("config.yaml", "unknown-key: invalid\n");
        var console = new TestConsole();
        using var application = new CliTestApplication(
            console,
            AboxPaths.For(root.Path, root.Path),
            new TrailingArguments()
        );
        var provider = application.Provider;

        // Act
        var exitCode = await provider.GetRequiredService<AboxRootCommand>().InvokeAsync([command]);

        // Assert
        exitCode.Should().Be(1);
        console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("error: ")
            .And.Contain("unknown-key");
    }
}
