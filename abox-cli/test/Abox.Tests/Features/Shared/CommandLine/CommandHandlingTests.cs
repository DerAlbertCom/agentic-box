using Abox.Features.Shared.CommandLine;

namespace Abox.Tests;

public sealed class CommandHandlingTests
{
    [Fact]
    public async Task CancellationUsesTheConventionalExitCode()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        var exitCode = await CommandHandling.RunAsync(
            () => throw new OperationCanceledException(),
            console
        );

        // Assert
        exitCode.Should().Be(130);
        console.Errors.Should().Equal("error: the command was cancelled.");
    }
}
