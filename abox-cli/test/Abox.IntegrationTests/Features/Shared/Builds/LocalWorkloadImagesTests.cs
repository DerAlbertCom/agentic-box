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
    public async Task BuildProducesARealUniqueLocalImageAndAThinV3Kit()
    {
        // Arrange: the fixture supplies isolated sources and a fake Docker runner.

        // Act
        var image = await BuildAsync("one");
        var next = await BuildAsync("two");

        // Assert
        image.Reference.Should().StartWith("docker.io/library/abox-workload:");
        image.Reference.Should().NotBe(next.Reference);
        image.ImageId.Should().Be(LocalWorkloadFactory.ImageId);
        image.ImageStore.Should().Be("test-context");
        File.ReadAllText(Path.Combine(image.RuntimeKitDirectory, "workload.dockerfile"))
            .Should()
            .Be($"FROM {image.Reference}\n");
        File.ReadAllText(Path.Combine(image.RuntimeKitDirectory, "workload.yaml"))
            .Should()
            .Be(File.ReadAllText(Path.Combine(Root.Path, "source", "workload.yaml")));
        await Runner
            .Received(2)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.Arguments.Contains("--load")
                    && request.Arguments.Contains(
                        Path.Combine(Root.Path, "source", "workload.yaml")
                    )
                    && request.Arguments.Contains("test-context")
                    && !request.Arguments.Contains("--push")
                ),
                Arg.Any<CancellationToken>()
            );
    }

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
    [InlineData("")]
    [InlineData("not-a-content-id")]
    public async Task UnprovenImageIdentityNeverProducesARuntimeKit(string contentId)
    {
        // Arrange
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("{{.Id}}")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, contentId, ""));

        // Act
        var build = () => BuildAsync("runtime");

        // Assert
        (await build.Should().ThrowAsync<AboxException>()).WithMessage(
            "*valid content ID*abox build*again*"
        );
        Directory.Exists(Path.Combine(Root.Path, "runtime")).Should().BeFalse();
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
    public async Task ARemoteBuilderIsNotUsedForHostOnlyImages()
    {
        // Arrange
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("ls")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProcessResult(
                    0,
                    "{\"Name\":\"test-context\",\"Driver\":\"docker-container\"}",
                    ""
                )
            );

        // Act
        var action = () => BuildAsync("one");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*docker-driver*docker-container*"
        );
        await Runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--load")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SourcesChangedWhileDockerBuildsAreNotRecorded()
    {
        // Arrange
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--load")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                File.AppendAllText(
                    Path.Combine(Root.Path, "source", "workload.yaml"),
                    "description: changed\n"
                );
                return new ProcessResult(0, "", "");
            });

        // Act
        var action = () => BuildAsync("one");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*sources changed*abox build*"
        );
        Directory.Exists(Path.Combine(Root.Path, "one")).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDockerStopsLocalBuildAndAvailabilityChecksBeforeReadingSources(
        bool checkAvailability
    )
    {
        // Arrange
        var programs = Substitute.For<IHostPrograms>();
        var images = LocalWorkloadFactory.Images(programs, Runner, new TestConsole());

        // Act
        Func<Task> action = checkAvailability
            ? () => images.EnsureAvailableAsync(new LocalWorkloadImage(), CancellationToken.None)
            : () => images.BuildAsync("missing-source", "missing-runtime", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
        );
        await Runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingBuildxStopsLocalBuildBeforeContextSelection()
    {
        // Arrange
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("version")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(17, "", "synthetic buildx failure"));

        // Act
        var action = () => BuildAsync("runtime");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Local workload image operation failed*exit 17*No source-build fallback*synthetic buildx failure*"
        );
        await Runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("context") || request.Arguments.Contains("--load")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private Task<LocalWorkloadImage> BuildAsync(string runtime) => _fixture.BuildAsync(runtime);
}
