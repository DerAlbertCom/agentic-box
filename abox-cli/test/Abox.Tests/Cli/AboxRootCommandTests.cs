using System.CommandLine;
using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class AboxRootCommandTests
{
    [Fact]
    public async Task TheExecutableIgnoresAppsettingsInTheWorkingDirectory()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("appsettings.json", "not json");
        root.File("abox/config.yaml", "unknown-key: invalid\n");
        // Act
        var result = await new ProcessRunner().RunAsync(
            new ProcessRequest(
                "dotnet",
                [typeof(AboxRootCommand).Assembly.Location, "--help"],
                WorkingDirectory: root.Path,
                Environment: new Dictionary<string, string>
                {
                    ["XDG_CONFIG_HOME"] = root.Path,
                    ["XDG_STATE_HOME"] = root.Path,
                }
            ),
            CancellationToken.None
        );

        // Assert
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("abox: AI agent sandboxes");
        result.StandardError.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--verbose", "build", "--team", "mti-ca")]
    [InlineData("build", "--verbose", "--team", "mti-ca")]
    [InlineData("build", "--team", "mti-ca", "--verbose")]
    [InlineData("secrets", "list", "--verbose")]
    public async Task VerboseIsAvailableBeforeOrAfterCommandsIncludingNestedCommands(
        params string[] args
    )
    {
        // Arrange
        var console = new TestConsole();
        var verbosity = new Verbosity();
        var output = new ProcessOutput(console, verbosity);
        var build = new Command("build");
        build.Options.Add(new Option<string>("--team"));
        build.SetAction(_ => ShowOutput());
        var secrets = new Command("secrets");
        var list = new Command("list");
        list.SetAction(_ => ShowOutput());
        secrets.Subcommands.Add(list);
        var root = new AboxRootCommand([build, secrets], verbosity);

        // Act
        var exitCode = await root.InvokeAsync(args);

        // Assert
        exitCode.Should().Be(17);
        console.AllText.Should().Contain("complete stdout").And.Contain("complete stderr");
        console.Output.Should().BeEmpty();

        int ShowOutput()
        {
            output.Write(
                new ProcessRequest("tool", []),
                new ProcessResult(17, "complete stdout", "complete stderr")
            );
            return 17;
        }
    }

    [Fact]
    public async Task VerboseIsOptInForEachInvocation()
    {
        // Arrange
        var verbosity = new Verbosity();
        var build = new Command("build");
        build.SetAction(_ => 0);
        var root = new AboxRootCommand([build], verbosity);

        // Act
        await root.InvokeAsync(["build", "--verbose"]);
        // Assert
        verbosity.Verbose.Should().BeTrue();
        // Act
        await root.InvokeAsync(["build"]);
        // Assert
        verbosity.Verbose.Should().BeFalse();
    }

    [Fact]
    public async Task VerboseAfterTheSeparatorBelongsToTheChildNotAbox()
    {
        // Arrange
        var verbosity = new Verbosity();
        var run = new Command("run");
        run.SetAction(_ => 0);
        var root = new AboxRootCommand([run], verbosity);
        var (arguments, trailing) = CommandLine.Split(["run", "--", "--verbose"]);

        // Act
        var exitCode = await root.InvokeAsync(arguments);

        // Assert
        exitCode.Should().Be(0);
        verbosity.Verbose.Should().BeFalse();
        trailing.Should().Equal("--verbose");
    }
}
