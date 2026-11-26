using Abox.Cli;
using Abox.Features.Publish;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishCommandTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly IHostPrograms _programs = Substitute.For<IHostPrograms>();
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly BuildStore _store;

    public PublishCommandTests()
    {
        _store = new BuildStore(AboxPaths.For(_root.Path, Path.Combine(_root.Path, "state")));
        _programs.Exists(Arg.Any<string>()).Returns(true);
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => PublishTestProcess.Run(call.Arg<ProcessRequest>()));
    }

    [Fact]
    public async Task TeamAndTagDefaultToTheUserConfiguration()
    {
        // Arrange
        SaveBuild("sample", "ac", "release");
        var config = new AboxConfig { DefaultTeam = "sample", DefaultTag = "release" };

        // Act
        var exitCode = await Publish(config, "--root", _root.Path);

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("agentic-box-rwest-sample/ac:release");
    }

    [Fact]
    public async Task TeamsCanBeRepeatedAndBuilderAndTagArePassedThrough()
    {
        // Arrange
        SaveBuild("one", "ac", "test");
        SaveBuild("two", "bd", "test");

        // Act
        var exitCode = await Publish(
            new AboxConfig(),
            "--team",
            "one",
            "--team",
            "two",
            "--root",
            _root.Path,
            "--tag",
            "test",
            "--builder",
            "publisher"
        );

        // Assert
        exitCode.Should().Be(0);
        _console
            .AllText.Should()
            .Contain("agentic-box-rwest-one/ac:test")
            .And.Contain("agentic-box-rwest-two/bd:test");
        await _runner
            .Received(6)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--builder")
                    && request.Arguments.Contains("publisher")
                    && request.Arguments.Contains("--push")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PublishingUsesTheRepositoryOfTheBuiltInheritedWorkload()
    {
        // Arrange
        _root.File("teams/sample/cape.abox", "workload: shared\n");
        _root.File("teams/sample/ac.abox", "base: cape\n");
        _root.File("kits/shared.workload", "publishRepository: shared-images-local\n");
        var config = new AboxConfig { DefaultTeam = "sample" };
        var resolver = new SquadResolver(config, new YamlDotNetParser());
        var build = TestServiceGraphs.Build(
            resolver,
            _store,
            LocalWorkloadFactory.Runner(),
            _console
        );
        await build.BuildAsync(
            new Features.Build.BuildRequest(
                [resolver.ResolveTeam(_root.Path, "sample")],
                _root.Path,
                "latest"
            ),
            CancellationToken.None
        );

        // Act
        var exitCode = await Publish(config, "--root", _root.Path);

        // Assert
        exitCode.Should().Be(0);
        _console
            .AllText.Should()
            .Contain("rwest.jfrog.io/shared-images-local/agentic-box-rwest-sample/ac:latest");
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Any(argument =>
                        argument.Contains("mtiae-developer-container-images-local")
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task NoTeamIsAnAboxErrorNotAnUnhandledException()
    {
        // Arrange: the fixture supplies a command with no configured default team.
        // Act
        var exitCode = await Publish(new AboxConfig());

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("No team is given");
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishingDoesNotRequireSbx()
    {
        // Arrange
        SaveBuild("sample", "ac", "latest");
        _programs.Exists("sbx").Returns(false);

        // Act
        var exitCode = await Publish(
            new AboxConfig { DefaultTeam = "sample" },
            "--root",
            _root.Path
        );

        // Assert
        exitCode.Should().Be(0);
        await _runner.Received().RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedPublishingReturnsOneWithTheDiagnostic()
    {
        // Arrange
        SaveBuild("sample", "ac", "latest");
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "buildx is unavailable"));

        // Act
        var exitCode = await Publish(
            new AboxConfig { DefaultTeam = "sample" },
            "--root",
            _root.Path
        );

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("buildx is unavailable");
    }

    private async Task<int> Publish(AboxConfig config, params string[] arguments)
    {
        using var application = new CliTestApplication(
            _console,
            AboxPaths.For(_root.Path, Path.Combine(_root.Path, "state")),
            new TrailingArguments()
        );
        application.Services.AddSingleton(config);
        application.Services.AddSingleton(
            new PublishService(
                _store,
                new DockerRegistryPublisher(_programs, _runner),
                _console,
                new YamlDotNetParser()
            )
        );
        return await application
            .Provider.GetRequiredService<AboxRootCommand>()
            .InvokeAsync(["publish", .. arguments]);
    }

    [Fact]
    public async Task EverySelectedTeamIsValidatedBeforeTheFirstPush()
    {
        // Arrange
        SaveBuild("one", "ac", "latest");
        _root.File("teams/two/bd.abox", "workload: sample\n");

        // Act
        var exitCode = await Publish(
            new AboxConfig(),
            "--team",
            "one",
            "--team",
            "two",
            "--root",
            _root.Path
        );

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("abox build --team two");
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private void SaveBuild(string team, string squad, string tag)
    {
        _root.File($"teams/{team}/{squad}.abox", "workload: sample\n");
        var workload = Path.Combine(
            _store.DirectoryFor(new SquadBuildIdentity(team, squad, tag)),
            "workload"
        );
        var manifest = new BuildManifest
        {
            Squad = squad,
            Tag = tag,
            Reference = $"agentic-box-rwest-{team}:{tag}",
            PublishRepository = "mtiae-developer-container-images-local",
            WorkloadKitDirectory = workload,
        };
        PublicationBuilds.SaveWorkload(
            _store,
            manifest,
            Path.Combine(workload, "workload.yaml"),
            "schemaVersion: \"3\"\nkind: workload\n",
            team
        );
    }
}
