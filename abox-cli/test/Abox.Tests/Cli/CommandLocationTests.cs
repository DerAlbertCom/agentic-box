using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class CommandLocationTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CreateAndRunResolveTheSameLocation(bool configured, bool explicitLocation)
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var config = new AboxConfig
        {
            DefaultTeam = "sample",
            DefaultLocation = configured ? root.CreateDirectory("default") : null,
        };
        var location = explicitLocation ? root.CreateDirectory("explicit") : null;
        var expected = location ?? config.DefaultLocation ?? Directory.GetCurrentDirectory();
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        var paths = AboxPaths.For(root.Path, root.Path);
        using var application = new CliTestApplication(
            console,
            paths,
            new TrailingArguments()
        ).WithSandboxSubstitutes();
        var lifecycle = application.Lifecycle;
        lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(""));
        application.Services.AddSingleton(new Lazy<AboxConfig>(() => config));
        application.Services.AddSingleton(programs);
        application.Services.AddSingleton(ReadyPreflight.Checks());
        application.Services.AddSingleton(PublishedSquadFactory.Source());
        var provider = application.Provider;
        var command = provider.GetRequiredService<AboxRootCommand>();
        string[] locationArguments = location is null ? [] : ["--location", location];

        // Act
        var createExitCode = await command.InvokeAsync([
            "create",
            "--squad",
            "ac",
            .. locationArguments,
        ]);

        // Assert
        createExitCode.Should().Be(0);

        // Act
        var runExitCode = await command.InvokeAsync(["run", .. locationArguments]);

        // Assert
        runExitCode.Should().Be(1);
        await lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request => request.Location == expected),
                Arg.Any<CancellationToken>()
            );
        await lifecycle.Received(2).ListAsync(Arg.Any<CancellationToken>());
        console
            .Errors.Should()
            .Contain(line => line.Contains($"There is no sandbox for '{expected}'"));
    }
}
