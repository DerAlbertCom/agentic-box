namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class SourceRootCommandTests
{
    [Fact]
    public async Task OutsideTheRepositoryBuildUsesTheSourceRootSetting()
    {
        // Arrange
        using var project = new SourceProjectFixture(sourceRootSetting: true);

        // Act
        var exitCode = await project.InvokeAsync(project.Workspace, "build");

        // Assert
        exitCode.Should().Be(0, project.Console.AllText);
        project.LoadBuild().RootDirectory.Should().Be(project.ProjectDirectory);
    }

    [Fact]
    public async Task OutsideTheRepositoryBuildAsksForTheSourceRootAndSavesIt()
    {
        // Arrange
        using var project = new SourceProjectFixture();
        project.Console.Lines.Add(project.ProjectDirectory);

        // Act
        var exitCode = await project.InvokeAsync(project.Workspace, "build");

        // Assert
        exitCode.Should().Be(0, project.Console.AllText);
        project.LoadBuild().RootDirectory.Should().Be(project.ProjectDirectory);
        File.ReadAllText(project.ConfigFile).Should().Contain(project.ProjectDirectory);
    }

    [Fact]
    public async Task OutsideTheRepositoryPublishWithoutAnAnswerNamesTheSettingAndTheOption()
    {
        // Arrange
        using var project = new SourceProjectFixture();
        var console = project.Console;

        // Act
        var exitCode = await project.InvokeAsync(project.Workspace, "publish");

        // Assert
        exitCode.Should().Be(1);
        console.Errors.Should().ContainSingle().Which.Should().Contain("'sourceRoot'");
        console.Errors.Single().Should().Contain("--root");
    }
}
