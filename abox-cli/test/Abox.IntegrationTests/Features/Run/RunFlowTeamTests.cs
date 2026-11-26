using System.Text.Json;
using Abox.Features.Run;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunFlowTeamTests
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

    [Theory]
    [InlineData("qualified")]
    [InlineData("legacy")]
    [InlineData("unknown-legacy")]
    [InlineData("corrupt-qualified")]
    public async Task UnrelatedOrUnprovenBuildsNeverOfferReplacementOrInterruptLaunch(
        string storage
    )
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
        var build = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            Reference = "agentic-box-rwest-other:latest",
            CreatedAt = DateTimeOffset.Now,
            LocalWorkload = LocalWorkloadFactory.Artifact(Root),
        };
        if (storage == "qualified")
            Store.Save(build);
        else
        {
            if (storage == "unknown-legacy")
                build.Reference = null;
            Root.File(
                storage == "corrupt-qualified"
                    ? "state/builds/sample/ac/latest/build.json"
                    : "state/builds/ac/latest/build.json",
                JsonSerializer.Serialize(build)
            );
        }

        // Act
        var exitCode = await Service.RunAsync(
            new RunRequest("shell", RunFlowFixture.Location(Root.Path), []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        Console.AllText.Should().NotContain("newer build").And.NotContain("Replace the sandbox");
        if (storage != "qualified")
            Console.AllText.Should().Contain("abox build --team sample --tag latest");
        await Exec.Received(1)
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementUsesTheRecordedTeamsBuildEvenWhenAnotherTeamsSharedSquadIsNewer(
        bool legacy
    )
    {
        // Arrange
        using var otherSources = new TempDirectory();
        var otherImage = LocalWorkloadFactory.Artifact(otherSources);
        var name = SandboxName.For(Root.Path);
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        Lifecycle
            .RemoveAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, ""));
        Lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(""));
        Mcp.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
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
        var selected = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            Reference = "agentic-box-rwest-sample:latest",
            RootDirectory = Root.Path,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LocalWorkload = LocalWorkloadFactory.Artifact(Root),
        };
        if (legacy)
            Root.File("state/builds/ac/latest/build.json", JsonSerializer.Serialize(selected));
        else
            Store.Save(selected);
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-other:latest",
                RootDirectory = otherSources.Path,
                CreatedAt = DateTimeOffset.UtcNow,
                LocalWorkload = otherImage,
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
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == selected.LocalWorkload.RuntimeKitDirectory
                ),
                Arg.Any<CancellationToken>()
            );
        Records.Load(name)!.Team.Should().Be("sample");
        Records.Load(name)!.BuildCreatedAt.Should().Be(selected.CreatedAt);
        await Exec.Received(1)
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateHintsNeverBorrowAnotherTeamsBuildOrSourceRoot(bool hasOwnedBuild)
    {
        // Arrange
        using var other = new TempDirectory();
        Records.Save(
            SandboxName.For(Root.Path),
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
            }
        );
        if (hasOwnedBuild)
            Store.Save(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = "agentic-box-rwest-sample:latest",
                    RootDirectory = Root.Path,
                }
            );
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-other:latest",
                RootDirectory = other.Path,
            }
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
            .Contain("--team sample --squad ac --tag latest")
            .And.NotContain(other.Path);
        if (hasOwnedBuild)
            Console.AllText.Should().Contain($"--local --root \"{Root.Path}\"");
        else
            Console.AllText.Should().NotContain("--local").And.NotContain("--root");
    }
}
