using Abox.Features.Run;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Launchers;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunFlowTests
{
    private readonly RunFlowFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private TestConsole Console => _fixture.Console;
    private ISandboxLifecycle Lifecycle => _fixture.Lifecycle;
    private IMcpServerRegistry Mcp => _fixture.Mcp;
    private ISandboxInspection Inspection => _fixture.Inspection;
    private ISandboxExec Exec => _fixture.Exec;
    private RunFlow Service => _fixture.Service;
    private BuildStore Store => _fixture.Store;
    private SandboxRecordStore Records => _fixture.Records;
    private SandboxCreator Create => _fixture.Create;

    [Fact]
    public async Task TheShellRunsInTheWorkspaceOfTheSandbox()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Inspection
            .GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>())
            .Returns("/home/agent/workspace");

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await Exec.Received(1)
            .RunAsync(
                Arg.Is<SandboxExecRequest>(request =>
                    request.Command.SequenceEqual(new[] { "bash", "-l" })
                    && request.WorkingDirectory == "/home/agent/workspace"
                    && request.Input == SandboxExecInput.Terminal
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheShellRunsInTheFolderWhenTheRuntimeDoesNotSayWhereTheWorkspaceIs()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Inspection
            .GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Act
        await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        await Exec.Received(1)
            .RunAsync(
                Arg.Is<SandboxExecRequest>(request => request.WorkingDirectory == Root.Path),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheShellRunsTheCommandThatIsGiven()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });

        // Act
        await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), ["ls", "-la"]),
            CancellationToken.None
        );

        // Assert
        await Exec.Received(1)
            .RunAsync(
                Arg.Is<SandboxExecRequest>(request =>
                    request.Command.SequenceEqual(new[] { "ls", "-la" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task WithoutASandboxTheCreateCommandIsPrinted()
    {
        // Arrange: the fixture has no existing sandbox.
        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console
            .AllText.Should()
            .Contain("abox create")
            .And.Contain("--squad <name>")
            .And.Contain("--team sample")
            .And.Contain("replace <name> placeholders");
        await Exec.DidNotReceive()
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCreateHintUsesTheSandboxRecord()
    {
        // Arrange
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "release",
                Reference = ImageName.OfTeam("sample", "release"),
            }
        );
        Records.Save(
            SandboxName.For(Root.Path),
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "release",
            }
        );

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console.AllText.Should().Contain("--team sample --squad ac --tag release --local");
    }

    [Fact]
    public async Task ARecordWithoutAKitReferenceDoesNotProveTheSandboxWasLocal()
    {
        // Arrange
        Records.Save(
            SandboxName.For(Root.Path),
            new SandboxRecord { Squad = "ac", Tag = "release" }
        );
        var config = new AboxConfig();
        var service = new RunFlow(
            config,
            new LauncherRegistry([new ShellLauncher()]),
            Store,
            Records,
            Create,
            Inspection,
            Exec,
            SandboxFactory.Https(Exec, Console),
            SandboxFactory.Resolver(Lifecycle, SandboxFactory.Hints(config, Store, Records)),
            SandboxFactory.Hints(config, Store, Records),
            Substitute.For<IHostPrograms>(),
            Console
        );

        // Act
        var exitCode = await service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console
            .AllText.Should()
            .Contain("--team <name> --squad ac --tag release")
            .And.Contain("replace <name> placeholders")
            .And.NotContain("--local")
            .And.NotContain("--root");
    }

    [Fact]
    public async Task TheCreateHintDoesNotGuessTheSquadFromTeamSources()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\n");

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console
            .AllText.Should()
            .Contain("--team sample --squad <name> --tag latest")
            .And.Contain("replace <name> placeholders");
    }

    [Fact]
    public async Task AnOldRecordCanRecoverTheTeamFromItsBuildMetadata()
    {
        // Arrange
        Records.Save(
            SandboxName.For(Root.Path),
            new SandboxRecord { Squad = "ac", Tag = "release" }
        );
        Root.File(
            "state/builds/ac/release/build.json",
            System.Text.Json.JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "release",
                    RootDirectory = Root.Path,
                    Reference = ImageName.OfTeam("other-team", "release"),
                }
            )
        );

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console
            .AllText.Should()
            .Contain("--team other-team --squad ac --tag release --local --location")
            .And.NotContain("--root");
    }

    [Fact]
    public async Task MissingLauncherProgramsPrintThePublishedSquadInTheCreateHint()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Inspection
            .HasProgramAsync(name, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        Records.Save(
            name,
            new SandboxRecord
            {
                Team = "other-team",
                Squad = "ac",
                Tag = "release",
                KitReference = "published-set",
            }
        );

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("app", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console
            .AllText.Should()
            .Contain("--team other-team --squad ac --tag release")
            .And.NotContain("--local");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownTeamAndSquadNamesAreClearlyMarkedInTheCreateHint(bool emptyRecord)
    {
        // Arrange
        if (emptyRecord)
        {
            Records.Save(SandboxName.For(Root.Path), new SandboxRecord());
        }

        var service = new RunFlow(
            new AboxConfig(),
            new LauncherRegistry([new ShellLauncher()]),
            Store,
            Records,
            Create,
            Inspection,
            Exec,
            SandboxFactory.Https(Exec, Console),
            SandboxFactory.Resolver(
                Lifecycle,
                SandboxFactory.Hints(new AboxConfig(), Store, Records)
            ),
            SandboxFactory.Hints(new AboxConfig(), Store, Records),
            Substitute.For<IHostPrograms>(),
            Console
        );

        // Act
        var exitCode = await service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        Console
            .AllText.Should()
            .Contain("--team <name> --squad <name>")
            .And.Contain("replace <name> placeholders");
    }

    [Fact]
    public async Task ALauncherThatIsNotKnownIsAnError()
    {
        // Arrange: the fixture registers only the shell and app launchers.
        // Act
        var action = () =>
            Service.RunAsync(
                new RunRequest("nope", RunFlowFixture.Location(Root.Path), []),
                CancellationToken.None
            );

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("no launcher 'nope'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewerBuildIsOfferedAndReplacesTheSandbox(bool separateSources)
    {
        // Arrange
        using var sources = new TempDirectory();
        var sourceRoot = separateSources ? sources : Root;
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Lifecycle
            .RemoveAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, string.Empty));
        Lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(string.Empty));
        Mcp.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        sourceRoot.File("teams/sample/ac.abox", "workload: sample\n");
        Records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                RootDirectory = sourceRoot.Path,
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                WorkloadKitDirectory = "/kits/workload",
                Reference = ImageName.OfTeam("sample", "latest"),
                LocalWorkload = LocalWorkloadFactory.Artifact(Root),
            }
        );

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        Console.AllText.Should().Contain("newer build");
        await Lifecycle.Received(1).RemoveAsync(name, Arg.Any<CancellationToken>());
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == Path.Combine(Root.Path, "runtime", "workload")
                    && request.PullPolicy == PullPolicy.Never
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task APulledSandboxIsNotReplacedByANewerLocalBuild()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = DateTimeOffset.MinValue,
                KitReference =
                    "rwest.jfrog.io/team-docker-release/agentic-box-rwest-sample/ac:latest",
            }
        );
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = DateTimeOffset.Now,
                Reference = ImageName.OfTeam("sample", "latest"),
            }
        );

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        Console.AllText.Should().NotContain("newer build");
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewerMetadataOnlyPreparationDoesNotInterruptAnExistingLocalSandbox()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = DateTimeOffset.MinValue,
            }
        );
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = ImageName.OfTeam("sample", "latest"),
                CreatedAt = DateTimeOffset.Now,
                LocalWorkload = null,
            }
        );

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        Console.AllText.Should().NotContain("newer build");
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Exec.Received(1)
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANewerBuildIsNotOfferedWhenTheAnswerIsNo()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Console.Answer = false;
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        Records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                WorkloadKitDirectory = "/kits/workload",
                Reference = ImageName.OfTeam("sample", "latest"),
                LocalWorkload = LocalWorkloadFactory.Artifact(Root),
            }
        );

        // Act
        await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
