using System.Text.Json;
using Abox.Features.Run;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunFlowTeamTests
{
    private readonly RunFlowFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private TestConsole _console => _fixture.Console;
    private ISbxClient _sbx => _fixture.Sbx;
    private ISandboxLifecycle _lifecycle => _fixture.Lifecycle;
    private IMcpServerRegistry _mcp => _fixture.Mcp;
    private ISandboxInspection _inspection => _fixture.Inspection;
    private ISandboxExec _exec => _fixture.Exec;
    private RunFlow _service => _fixture.Service;
    private BuildStore _store => _fixture.Store;
    private SandboxRecordStore _records => _fixture.Records;

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
        var name = SandboxName.For(_root.Path);
        _lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        _records.Save(
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
            LocalWorkload = LocalWorkloadFactory.Artifact(_root),
        };
        if (storage == "qualified")
            _store.Save(build);
        else
        {
            if (storage == "unknown-legacy")
                build.Reference = null;
            _root.File(
                storage == "corrupt-qualified"
                    ? "state/builds/sample/ac/latest/build.json"
                    : "state/builds/ac/latest/build.json",
                JsonSerializer.Serialize(build)
            );
        }

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("newer build").And.NotContain("Replace the sandbox");
        if (storage != "qualified")
            _console.AllText.Should().Contain("abox build --team sample --tag latest");
        await _exec
            .Received(1)
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
        await _lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _lifecycle
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
        var name = SandboxName.For(_root.Path);
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        _lifecycle
            .RemoveAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, ""));
        _lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(""));
        _mcp.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        _records.Save(
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
            RootDirectory = _root.Path,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            LocalWorkload = LocalWorkloadFactory.Artifact(_root),
        };
        if (legacy)
            _root.File("state/builds/ac/latest/build.json", JsonSerializer.Serialize(selected));
        else
            _store.Save(selected);
        _store.Save(
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
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("newer build");
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == selected.LocalWorkload.RuntimeKitDirectory
                ),
                Arg.Any<CancellationToken>()
            );
        _records.Load(name)!.Team.Should().Be("sample");
        _records.Load(name)!.BuildCreatedAt.Should().Be(selected.CreatedAt);
        await _exec
            .Received(1)
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateHintsNeverBorrowAnotherTeamsBuildOrSourceRoot(bool hasOwnedBuild)
    {
        // Arrange
        using var other = new TempDirectory();
        _records.Save(
            SandboxName.For(_root.Path),
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
            }
        );
        if (hasOwnedBuild)
            _store.Save(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = "agentic-box-rwest-sample:latest",
                    RootDirectory = _root.Path,
                }
            );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-other:latest",
                RootDirectory = other.Path,
            }
        );

        // Act
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(1);
        _console
            .AllText.Should()
            .Contain("--team sample --squad ac --tag latest")
            .And.NotContain(other.Path);
        if (hasOwnedBuild)
            _console.AllText.Should().Contain($"--local --root \"{_root.Path}\"");
        else
            _console.AllText.Should().NotContain("--local").And.NotContain("--root");
    }
}
