using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class DockerComponentImagesTests
{
    private readonly IProcessRunner _runner = LocalWorkloadFactory.Runner();

    private DockerComponentImages Images() => new(Substitute.For<IHostPrograms>(), _runner);

    private void Builders(string output) =>
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("ls")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, output, ""));

    [Fact]
    public async Task TheHostContextIsReturnedWhenItsBuilderSharesTheHostImageStore()
    {
        // Act
        var context = await Images().HostImageStoreAsync(CancellationToken.None);

        // Assert
        context.Should().Be("test-context");
        Received.InOrder(() =>
        {
            _runner.RunAsync(Docker("buildx", "version"), CancellationToken.None);
            _runner.RunAsync(Docker("context", "show"), CancellationToken.None);
            _runner.RunAsync(Docker("buildx", "ls", "--format", "json"), CancellationToken.None);
        });
    }

    [Theory]
    [InlineData("{\"Name\":\"test-context\",\"Driver\":\"docker-container\"}", "docker-container")]
    [InlineData("{\"Name\":\"other\",\"Driver\":\"docker\"}", "")]
    public async Task OnlyASingleDockerDriverBuilderSharesTheHostImageStore(
        string builders,
        string drivers
    )
    {
        // Arrange
        Builders(builders);

        // Act
        var action = () => Images().RequireHostBuilderAsync("test-context", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            $"Local workload images require the docker-driver builder for context 'test-context', not '{drivers}'.*"
        );
    }

    [Fact]
    public async Task UnreadableBuilderListsAreReportedAsBuildxProblems()
    {
        // Arrange
        Builders("not json");

        // Act
        var action = () => Images().RequireHostBuilderAsync("test-context", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Cannot read Docker Buildx builders.*"
        );
    }

    [Fact]
    public async Task BuildingLoadsTheAmd64ImageIntoTheContextsOwnStoreAndReturnsItsDigest()
    {
        // Arrange
        using var work = new TempDirectory();
        const string reference = "rwest.jfrog.io/repo/team/squad/components/workload:1";

        // Act
        var digest = await Images()
            .BuildAsync(
                new ComponentImageBuild(
                    "source",
                    "source/workload.yaml",
                    reference,
                    "ctx",
                    work.Path,
                    WithGitHubToken: false
                ),
                CancellationToken.None
            );

        // Assert
        digest.Should().Be(LocalWorkloadFactory.Digest(reference));
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request
                        .Arguments.Take(12)
                        .SequenceEqual(
                            new[]
                            {
                                "--context",
                                "ctx",
                                "buildx",
                                "build",
                                "--builder",
                                "ctx",
                                "--platform",
                                "linux/amd64",
                                "--file",
                                "source/workload.yaml",
                                "--tag",
                                reference,
                            }
                        )
                    && request.Arguments.Contains("--provenance=false")
                    && request.Arguments.Contains("--load")
                    && request.Arguments.Last() == "source"
                ),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task AnImageTheStoreDoesNotHoldHasNoImageId()
    {
        // Arrange
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("{{.Id}}")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(1, "", "No such image"));

        // Act
        var imageId = await Images().FindImageIdAsync("ctx", "missing:1", CancellationToken.None);

        // Assert
        imageId.Should().BeNull();
    }

    private static ProcessRequest Docker(params string[] arguments) =>
        Arg.Is<ProcessRequest>(request =>
            request.FileName == "docker" && request.Arguments.SequenceEqual(arguments)
        );
}
