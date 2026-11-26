using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class BuildDiscoveryTests
{
    [Theory]
    [InlineData("teams", "kits")]
    [InlineData("config/teams", "shared/kits")]
    public async Task NestedBuildUsesTheSameProjectKitsAndWorkloadAsTheRoot(
        string teams,
        string kits
    )
    {
        // Arrange
        using var project = new SourceProjectFixture(teams, kits);
        string[] arguments = ["build"];

        // Act
        var rootExit = await project.InvokeAsync(project.ProjectDirectory, arguments);
        var fromRoot = project.LoadBuild();
        var nestedExit = await project.InvokeAsync(project.NestedDirectory, arguments);
        var fromNested = project.LoadBuild();

        // Assert
        rootExit.Should().Be(0, project.Console.AllText);
        nestedExit.Should().Be(0, project.Console.AllText);
        fromNested.RootDirectory.Should().Be(project.ProjectDirectory);
        fromNested.Kits.Should().BeEquivalentTo(fromRoot.Kits);
        fromNested.ConfigFiles.Should().Equal(fromRoot.ConfigFiles);
        fromNested
            .Kits.Select(kit => Path.GetFullPath(kit.Reference))
            .Should()
            .Equal(project.ProjectKit);
        fromNested
            .Components.Select(component => component.Digest)
            .Should()
            .Equal(fromRoot.Components.Select(component => component.Digest));
        File.ReadAllText(Path.Combine(fromNested.WorkloadKitDirectory, "scripts", "setup.sh"))
            .Should()
            .Contain("owning-project-workload");
    }

    [Fact]
    public async Task RepeatedExplicitTeamsReplaceTheDefaultRatherThanAddingIt()
    {
        // Arrange
        using var project = new SourceProjectFixture();
        project.Root.File(
            "project/teams/one/first.abox",
            "workload: shared\nkits: [project-kit]\n"
        );
        project.Root.File(
            "project/teams/two/second.abox",
            "workload: shared\nkits: [project-kit]\n"
        );

        // Act
        var exitCode = await project.InvokeAsync(
            project.NestedDirectory,
            "build",
            "--team",
            "one",
            "--team",
            "two"
        );

        // Assert
        exitCode.Should().Be(0, project.Console.AllText);
        project.LoadBuild("first", "one").Reference.Should().Be("agentic-box-rwest-one:release");
        project.LoadBuild("second", "two").Reference.Should().Be("agentic-box-rwest-two:release");
        project.LoadBuild().Should().BeNull("explicit selections replace defaultTeam 'sample'");
    }
}
