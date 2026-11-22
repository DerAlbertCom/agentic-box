using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.KitInspection;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildServiceTeamTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private IKitInspector Inspector => _fixture.Inspector;
    private IProcessRunner Runner => _fixture.Runner;
    private BuildStore Store => _fixture.Store;
    private SquadResolver Resolver => _fixture.Resolver;
    private BuildService Service => _fixture.Service;

    [Theory]
    [InlineData("sample", "other", false)]
    [InlineData("other", "sample", false)]
    [InlineData("sample", "other", true)]
    [InlineData("other", "sample", true)]
    public async Task SharedSquadBuildsKeepEachTeamsGeneratedSourcesAndImage(
        string first,
        string second,
        bool metadataOnly
    )
    {
        // Arrange
        Root.File("teams/other/ac.abox", "workload: other\n");
        Root.File("teams/other/other.workload", "template: example.com/other:2\n");
        BuildRequest TeamRequest(string team) =>
            new(
                [Resolver.ResolveTeam(Root.Path, team)],
                Root.Path,
                "latest",
                Verify: false,
                IncludePersonalConfigurations: false,
                MetadataOnly: metadataOnly
            );

        // Act
        await Service.BuildAsync(TeamRequest(first), CancellationToken.None);
        var firstBuild = Store.Load(new SquadBuildIdentity(first, "ac", "latest"))!;
        var recipe = Path.Combine(firstBuild.WorkloadKitDirectory, "workload.dockerfile");
        var originalRecipe = File.ReadAllText(recipe);
        await Service.BuildAsync(TeamRequest(second), CancellationToken.None);
        var secondBuild = Store.Load(new SquadBuildIdentity(second, "ac", "latest"))!;

        // Assert
        File.ReadAllText(recipe).Should().Be(originalRecipe);
        secondBuild.WorkloadKitDirectory.Should().NotBe(firstBuild.WorkloadKitDirectory);
        Store
            .Load(new SquadBuildIdentity(first, "ac", "latest"))!
            .Reference.Should()
            .Be($"agentic-box-rwest-{first}:latest");
        Store
            .Load(new SquadBuildIdentity(second, "ac", "latest"))!
            .Reference.Should()
            .Be($"agentic-box-rwest-{second}:latest");
        BuildSources.EnsureUnchanged(firstBuild);
        BuildSources.EnsureUnchanged(secondBuild);
        if (metadataOnly)
        {
            firstBuild.LocalWorkload.Should().BeNull();
            secondBuild.LocalWorkload.Should().BeNull();
        }
        else
        {
            firstBuild.LocalWorkload.Should().NotBeNull();
            secondBuild.LocalWorkload.Should().NotBeNull();
            secondBuild
                .LocalWorkload!.Reference.Should()
                .NotBe(firstBuild.LocalWorkload!.Reference);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MultiTeamBuildsKeepSharedSquadsIndependent(bool reverse, bool metadataOnly)
    {
        // Arrange
        Root.File("teams/other/ac.abox", "workload: other\nstate: [~/.other]\n");
        Root.File("teams/other/other.workload", "template: example.com/other:2\n");
        Root.File("teams/sample/ac.abox", "workload: sample\nstate: [~/.sample]\n");
        var teams = new[] { "sample", "other" };
        if (reverse)
            Array.Reverse(teams);

        // Act
        var exitCode = await Service.BuildAsync(
            new BuildRequest(
                teams.Select(team => Resolver.ResolveTeam(Root.Path, team)).ToList(),
                Root.Path,
                "latest",
                Verify: false,
                IncludePersonalConfigurations: false,
                MetadataOnly: metadataOnly
            ),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        var sample = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var other = Store.Load(new SquadBuildIdentity("other", "ac", "latest"))!;
        File.ReadAllText(Path.Combine(sample.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain("example.com/base:1")
            .And.NotContain("example.com/other:2");
        File.ReadAllText(Path.Combine(other.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain("example.com/other:2")
            .And.NotContain("example.com/base:1");
        File.ReadAllText(Path.Combine(sample.StateMixinDirectory!, "state.yaml"))
            .Should()
            .Contain(".sample")
            .And.NotContain(".other");
        File.ReadAllText(Path.Combine(other.StateMixinDirectory!, "state.yaml"))
            .Should()
            .Contain(".other")
            .And.NotContain(".sample");
        BuildSources.EnsureUnchanged(sample);
        BuildSources.EnsureUnchanged(other);
        if (!metadataOnly)
        {
            sample.LocalWorkload!.Reference.Should().NotBe(other.LocalWorkload!.Reference);
            sample
                .LocalWorkload.RuntimeKitDirectory.Should()
                .NotBe(other.LocalWorkload.RuntimeKitDirectory);
        }
    }

    [Fact]
    public async Task MetadataOnlyInvalidatesOnlyTheSelectedTeamsImageAndLeavesLegacyArtifactsUntouched()
    {
        // Arrange
        Root.File("teams/other/ac.abox", "workload: other\n");
        Root.File("teams/other/other.workload", "template: example.com/other:2\n");
        var legacy = Root.File("state/builds/ac/latest/build.json", "legacy sentinel");
        var request = new BuildRequest(
            [Resolver.ResolveTeam(Root.Path, "sample"), Resolver.ResolveTeam(Root.Path, "other")],
            Root.Path,
            "latest",
            Verify: false,
            IncludePersonalConfigurations: false
        );
        await Service.BuildAsync(request, CancellationToken.None);
        var previous = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.LocalWorkload!;
        var other = Store.Load(new SquadBuildIdentity("other", "ac", "latest"))!;
        Runner.ClearReceivedCalls();
        Inspector.ClearReceivedCalls();

        // Act
        await Service.BuildAsync(
            request with
            {
                Teams = [request.Teams[0]],
                MetadataOnly = true,
            },
            CancellationToken.None
        );

        // Assert
        Store
            .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            .LocalWorkload.Should()
            .BeNull();
        Store
            .Load(new SquadBuildIdentity("other", "ac", "latest"))!
            .LocalWorkload!.Reference.Should()
            .Be(other.LocalWorkload!.Reference);
        File.Exists(Path.Combine(previous.RuntimeKitDirectory, "workload.yaml")).Should().BeTrue();
        File.ReadAllText(legacy).Should().Be("legacy sentinel");
        BuildSources.EnsureUnchanged(other);
        Runner.ReceivedCalls().Should().BeEmpty();
        Inspector.ReceivedCalls().Should().BeEmpty();
    }
}
