using Abox.Features.Shared.Builds;
using NSubstitute;

namespace Abox.Tests;

public sealed class LocalWorkloadImageSeamTests : IDisposable
{
    private static readonly string ImageId = "sha256:" + new string('d', 64);
    private readonly TempDirectory _root = new();
    private readonly IWorkloadImageBuilder _builder = Substitute.For<IWorkloadImageBuilder>();
    private readonly ILocalWorkloadImageStore _store = Substitute.For<ILocalWorkloadImageStore>();
    private readonly LocalWorkloadImages _images;

    public LocalWorkloadImageSeamTests()
    {
        _images = new LocalWorkloadImages(_builder, _store, new TestConsole());
        _store.IsInstalled.Returns(true);
        _builder.HostContextAsync(Arg.Any<CancellationToken>()).Returns("host");
        _store
            .ImageIdAsync("host", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ImageId);
        _root.File("source/workload.yaml", "kind: workload\n");
    }

    public void Dispose() => _root.Dispose();

    private Task<LocalWorkloadImage> BuildAsync() =>
        _images.BuildAsync(
            Path.Combine(_root.Path, "source"),
            Path.Combine(_root.Path, "runtime"),
            CancellationToken.None
        );

    [Fact]
    public async Task BuildRecordsTheHostContextAndTheImageIdOfTheBuiltImage()
    {
        // Act
        var image = await BuildAsync();

        // Assert
        image.DockerContext.Should().Be("host");
        image.ImageId.Should().Be(ImageId);
        await _builder
            .Received(1)
            .BuildAsync(
                Arg.Is<WorkloadImageBuild>(build =>
                    build.Context == "host"
                    && build.Reference == image.Reference
                    && build.Descriptor == Path.Combine(_root.Path, "source", "workload.yaml")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AvailabilityChecksTheRecordedContextsBuilderBeforeTheImage()
    {
        // Arrange
        var image = await BuildAsync();
        _store.ClearReceivedCalls();

        // Act
        await _images.EnsureAvailableAsync(image, CancellationToken.None);

        // Assert
        Received.InOrder(() =>
        {
            _store.RequireHostBuilderAsync("host", Arg.Any<CancellationToken>());
            _store.ImageIdAsync("host", image.Reference, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task AReplacedImageIsNotAvailable()
    {
        // Arrange
        var image = await BuildAsync();
        _store
            .ImageIdAsync("host", image.Reference, Arg.Any<CancellationToken>())
            .Returns("sha256:" + new string('e', 64));

        // Act
        var action = () => _images.EnsureAvailableAsync(image, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            $"The prebuilt workload image '{image.Reference}' was replaced. Run 'abox build' again."
        );
    }
}
