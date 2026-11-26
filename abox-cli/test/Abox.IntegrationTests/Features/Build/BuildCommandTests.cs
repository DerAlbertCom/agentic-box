using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class BuildCommandTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 3)]
    public async Task TheRebuildOptionBuildsEveryComponentImageAgain(bool rebuild, int expected)
    {
        // Arrange
        using var project = new SourceProjectFixture();
        var buildExit = await project.InvokeAsync(project.ProjectDirectory, "build");
        project.Runner.ClearReceivedCalls();
        string[] arguments = rebuild ? ["build", "--rebuild"] : ["build"];

        // Act
        var exitCode = await project.InvokeAsync(project.ProjectDirectory, arguments);

        // Assert
        buildExit.Should().Be(0, project.Console.AllText);
        exitCode.Should().Be(0, project.Console.AllText);
        project
            .Runner.ReceivedCalls()
            .Select(call => (ProcessRequest)call.GetArguments()[0]!)
            .Count(request => request.Arguments.Contains("--load"))
            .Should()
            .Be(expected);
    }
}
