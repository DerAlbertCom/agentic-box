using Abox.Features.Shared.Common;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class DockerRegistryDigestsTests
{
    private readonly IProcessRunner _runner = LocalWorkloadFactory.Runner();

    [Fact]
    public async Task AReferenceResolvesToTheDigestBuildxReportsForIt()
    {
        // Act
        var digest = await new DockerRegistryDigests(_runner).DigestAsync(
            "example.com/base:1",
            CancellationToken.None
        );

        // Assert
        digest.Should().Be(LocalWorkloadFactory.Digest("example.com/base:1"));
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.Arguments.SequenceEqual(
                        new[]
                        {
                            "buildx",
                            "imagetools",
                            "inspect",
                            "--format",
                            "{{json .Manifest}}",
                            "example.com/base:1",
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnUnreadableDigestFails()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "{\"digest\":\"not-a-digest\"}", ""));

        // Act
        var action = () =>
            new DockerRegistryDigests(_runner).DigestAsync(
                "example.com/base:1",
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Docker did not report a valid digest for 'example.com/base:1'."
        );
    }

    [Fact]
    public async Task AComponentImageIsPushedFromTheImageStoreOfItsBuild()
    {
        // Arrange
        var publisher = new DockerRegistryPublisher(LocalWorkloadFactory.Programs(), _runner);

        // Act
        await publisher.PushImageAsync(
            "ctx",
            "rwest.jfrog.io/repo/a/components/workload:1",
            CancellationToken.None
        );

        // Assert
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[]
                        {
                            "--context",
                            "ctx",
                            "push",
                            "rwest.jfrog.io/repo/a/components/workload:1",
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
