using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
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
        _builder.HostImageStoreAsync(Arg.Any<CancellationToken>()).Returns("host");
        _builder.NewImageReference().Returns("workload:new");
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
    public async Task BuildRecordsTheHostImageStoreAndTheImageIdOfTheBuiltImage()
    {
        // Act
        var image = await BuildAsync();

        // Assert
        image.ImageStore.Should().Be("host");
        image.Reference.Should().Be("workload:new");
        image.ImageId.Should().Be(ImageId);
        await _builder
            .Received(1)
            .BuildAsync(
                Arg.Is<WorkloadImageBuild>(build =>
                    build.ImageStore == "host"
                    && build.Reference == "workload:new"
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
