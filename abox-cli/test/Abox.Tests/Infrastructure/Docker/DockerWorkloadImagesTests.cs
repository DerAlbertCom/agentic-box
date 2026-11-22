using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class DockerWorkloadImagesTests
{
    private readonly IProcessRunner _runner = LocalWorkloadFactory.Runner();

    private DockerWorkloadImages Images() => new(Substitute.For<IHostPrograms>(), _runner);

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
    public async Task BuildingLoadsTheImageIntoTheContextsOwnStore()
    {
        // Act
        await Images()
            .BuildAsync(
                new WorkloadImageBuild("source", "source/workload.yaml", "abox-workload:1", "ctx"),
                CancellationToken.None
            );

        // Assert
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
                                "--file",
                                "source/workload.yaml",
                                "--tag",
                                "abox-workload:1",
                                "--provenance=false",
                                "--load",
                            }
                        )
                    && request.Arguments.Last() == "source"
                ),
                CancellationToken.None
            );
    }

    private static ProcessRequest Docker(params string[] arguments) =>
        Arg.Is<ProcessRequest>(request =>
            request.FileName == "docker" && request.Arguments.SequenceEqual(arguments)
        );
}
