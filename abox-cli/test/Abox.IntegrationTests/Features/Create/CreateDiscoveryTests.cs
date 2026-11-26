using Abox.Features.Shared.Sandboxes;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class CreateDiscoveryTests
{
    [Theory]
    [InlineData("inside-the-project")]
    [InlineData("another-workspace")]
    public async Task LocalCreateUsesTheBuildsKitsWhereverItRunsAndReadsNoTeamSources(string mode)
    {
        // Arrange
        using var project = new SourceProjectFixture("config/teams", "shared/kits");
        var buildExit = await project.InvokeAsync(project.ProjectDirectory, "build");
        var kits = Path.Combine(
            Path.GetDirectoryName(project.LoadBuild().WorkloadKitDirectory)!,
            LocalSquadSource.ThinKitsFolderName
        );
        project.Root.File("workspace/config/teams/sample/wrong.abox", "workload: wrong\n");
        var location = mode == "inside-the-project" ? project.NestedDirectory : project.Workspace;

        // Act
        var exitCode = await project.InvokeAsync(
            project.Root.Path,
            ["create", "--local", "--squad", "ac", "--location", location]
        );

        // Assert
        buildExit.Should().Be(0, project.Console.AllText);
        exitCode.Should().Be(0, project.Console.AllText);
        await project
            .Lifecycle.Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == Path.Combine(kits, "workload")
                    && request.Location == location
                    && request.Mixins.SequenceEqual(
                        new[]
                        {
                            Path.Combine(kits, "kits-project-kit"),
                            Path.Combine(kits, "config"),
                        }
                    )
                    && request.PullPolicy == PullPolicy.Never
                ),
                Arg.Any<CancellationToken>()
            );
        project.Console.AllText.Should().NotContain("There is no squad");
    }
}
