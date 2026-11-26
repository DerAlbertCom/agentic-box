using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class LocalWorkloadImagesTests : IDisposable
{
    private readonly LocalWorkloadImagesFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private IProcessRunner Runner => _fixture.Runner;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task CheckingAvailabilityDoesNotBuildOrPull()
    {
        // Arrange
        var image = await BuildAsync("one");
        Runner.ClearReceivedCalls();

        // Act
        await LocalWorkloadFactory
            .Images(Runner)
            .EnsureAvailableAsync(image, CancellationToken.None);

        // Assert
        await Runner.Received(2).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
        await Runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("build") || request.Arguments.Contains("pull")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AvailabilityChecksUseTheRecordedImageAndContext()
    {
        // Arrange
        var image = await BuildAsync("runtime");
        Runner.ClearReceivedCalls();

        // Act
        await LocalWorkloadFactory
            .Images(Runner)
            .EnsureAvailableAsync(image, CancellationToken.None);

        // Assert
        await Runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[]
                        {
                            "--context",
                            "test-context",
                            "image",
                            "inspect",
                            "--format",
                            "{{.Id}}",
                            image.Reference,
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrReplacedLocalContentRequiresRebuilding(bool missing)
    {
        // Arrange
        var image = await BuildAsync("one");
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("{{.Id}}")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                missing
                    ? new ProcessResult(1, "", "No such image")
                    : new ProcessResult(0, "sha256:" + new string('b', 64), "")
            );

        // Act
        var action = () =>
            LocalWorkloadFactory.Images(Runner).EnsureAvailableAsync(image, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*abox build*again*");
    }

    [Fact]
    public async Task ChangedRuntimeRecipesAreRejectedBeforeAnyDockerOperation()
    {
        // Arrange
        var image = await BuildAsync("one");
        File.AppendAllText(
            Path.Combine(image.RuntimeKitDirectory, "workload.dockerfile"),
            "RUN expensive-install\n"
        );
        Runner.ClearReceivedCalls();

        // Act
        var action = () =>
            LocalWorkloadFactory.Images(Runner).EnsureAvailableAsync(image, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*runtime kit changed*abox build*"
        );
        await Runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingDockerStopsAvailabilityChecksBeforeReadingSources()
    {
        // Arrange
        var programs = Substitute.For<IHostPrograms>();
        var images = LocalWorkloadFactory.Images(programs, Runner);

        // Act
        var action = () =>
            images.EnsureAvailableAsync(new LocalWorkloadImage(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
        );
        await Runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private Task<LocalWorkloadImage> BuildAsync(string runtime) => _fixture.BuildAsync(runtime);
}
