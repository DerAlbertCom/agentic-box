using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class DockerRegistryLoginTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();

    private void Exits(int exitCode) =>
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, "", ""));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task TheCheckTriesOnlyTheStoredCredentials(int exitCode, bool signedIn)
    {
        // Arrange
        Exits(exitCode);

        // Act
        var result = await new DockerRegistryLogin(_runner).IsSignedInAsync(CancellationToken.None);

        // Assert
        result.Should().Be(signedIn);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.Arguments.SequenceEqual(new[] { "login", "rwest.jfrog.io" })
                    && !request.AttachConsole
                    && request.StandardInput == ""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task TheSignInRunsDockerLoginOnTheTerminal(int exitCode, bool signedIn)
    {
        // Arrange
        Exits(exitCode);

        // Act
        var result = await new DockerRegistryLogin(_runner).SignInAsync(CancellationToken.None);

        // Assert
        result.Should().Be(signedIn);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.Arguments.SequenceEqual(new[] { "login", "rwest.jfrog.io" })
                    && request.AttachConsole
                    && request.StandardInput == null
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
