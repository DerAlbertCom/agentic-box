using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using NSubstitute;

namespace Abox.Tests;

public sealed class LocalWorkloadImageSeamTests : IDisposable
{
    private static readonly string ImageId = "sha256:" + new string('d', 64);
    private readonly TempDirectory _root = new();
    private readonly ILocalImageStore _store = Substitute.For<ILocalImageStore>();
    private readonly LocalWorkloadImages _images;

    public LocalWorkloadImageSeamTests()
    {
        _images = new LocalWorkloadImages(_store);
        _store.IsInstalled.Returns(true);
        _store
            .ImageIdAsync("host", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ImageId);
        _root.File("source/workload.yaml", "kind: workload\n");
    }

    public void Dispose() => _root.Dispose();

    private Task<LocalWorkloadImage> BuildAsync()
    {
        var runtime = Path.Combine(_root.Path, "runtime");
        LocalWorkloadImages.WriteRuntimeKit(
            Path.Combine(_root.Path, "source"),
            runtime,
            "workload:1"
        );
        return Task.FromResult(
            new LocalWorkloadImage
            {
                Reference = "workload:1",
                ImageId = ImageId,
                ImageStore = "host",
                RuntimeKitDirectory = runtime,
                RuntimeKitHash = BuildSources.DirectoryHash(runtime),
            }
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
