using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildServiceTeamTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private IProcessRunner Runner => _fixture.Runner;
    private BuildStore Store => _fixture.Store;
    private SquadResolver Resolver => _fixture.Resolver;
    private BuildService Service => _fixture.Service;

    [Theory]
    [InlineData("sample", "other")]
    [InlineData("other", "sample")]
    public async Task SharedSquadBuildsKeepEachTeamsGeneratedSourcesAndImage(
        string first,
        string second
    )
    {
        // Arrange
        Root.File("teams/other/ac.abox", "workload: other\n");
        Root.File(
            "teams/other/other.workload",
            "template: example.com/other:2\npublishRepository: test-images-local\n"
        );
        BuildRequest TeamRequest(string team) =>
            new([Resolver.ResolveTeam(Root.Path, team)], Root.Path, "latest");

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
        secondBuild.Components[0].Reference.Should().NotBe(firstBuild.Components[0].Reference);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultiTeamBuildsKeepSharedSquadsIndependent(bool reverse)
    {
        // Arrange
        Root.File("teams/other/ac.abox", "workload: other\nstate: [~/.other]\n");
        Root.File(
            "teams/other/other.workload",
            "template: example.com/other:2\npublishRepository: test-images-local\n"
        );
        Root.File("teams/sample/ac.abox", "workload: sample\nstate: [~/.sample]\n");
        var teams = new[] { "sample", "other" };
        if (reverse)
            Array.Reverse(teams);

        // Act
        var exitCode = await Service.BuildAsync(
            new BuildRequest(
                teams.Select(team => Resolver.ResolveTeam(Root.Path, team)).ToList(),
                Root.Path,
                "latest"
            ),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        var sample = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var other = Store.Load(new SquadBuildIdentity("other", "ac", "latest"))!;
        File.ReadAllText(Path.Combine(sample.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain("example.com/base@")
            .And.NotContain("example.com/other");
        File.ReadAllText(Path.Combine(other.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain("example.com/other@")
            .And.NotContain("example.com/base");
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
        sample.Components[0].Reference.Should().NotBe(other.Components[0].Reference);
    }
}
