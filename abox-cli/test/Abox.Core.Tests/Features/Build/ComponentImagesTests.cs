using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using NSubstitute;

namespace Abox.Tests;

public sealed class ComponentImagesTests : IDisposable
{
    private const string Repository = "rwest.jfrog.io/repo/agentic-box-rwest-sample/ac";
    private static readonly string ImageId = "sha256:" + new string('d', 64);
    private static readonly string Digest = "sha256:" + new string('e', 64);
    private readonly TempDirectory _root = new();
    private readonly IComponentImageBuilder _builder = Substitute.For<IComponentImageBuilder>();
    private readonly ILocalImageStore _store = Substitute.For<ILocalImageStore>();
    private readonly ComponentImages _images;
    private readonly ComponentSource _source;

    public ComponentImagesTests()
    {
        _images = new ComponentImages(_builder, _store, new TestConsole());
        var descriptor = _root.File("state/state.yaml", "kind: mixin\n");
        _source = new ComponentSource(
            "state",
            ComponentKind.Mixin,
            Path.GetDirectoryName(descriptor)!,
            descriptor,
            new Dictionary<string, string>()
        );
        _builder
            .BuildAsync(Arg.Any<ComponentImageBuild>(), Arg.Any<CancellationToken>())
            .Returns(Digest);
        _store
            .ImageIdAsync("host", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ImageId);
        _store
            .FindImageIdAsync("host", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ImageId);
    }

    public void Dispose() => _root.Dispose();

    private static ComponentTarget Target(
        IReadOnlyList<BuildComponent>? previous = null,
        bool rebuild = false
    ) => new("host", Repository, "work", previous ?? [], rebuild);

    [Fact]
    public async Task ABuiltComponentIsNamedWithItsRegistryNameAndTaggedWithItsInputHash()
    {
        // Act
        var component = await _images.BuildAsync(Target(), _source, CancellationToken.None);

        // Assert
        component.Reference.Should().Be($"{Repository}/components/state:{_source.InputHash}");
        component.Digest.Should().Be(Digest);
        component.ImageId.Should().Be(ImageId);
        component.Descriptor.Should().Be("kind: mixin\n");
        await _builder
            .Received(1)
            .BuildAsync(
                Arg.Is<ComponentImageBuild>(build =>
                    build.Reference == component.Reference
                    && build.ImageStore == "host"
                    && build.Descriptor == _source.Descriptor
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnUnchangedComponentThatTheStoreStillHoldsIsReused()
    {
        // Arrange
        var previous = await _images.BuildAsync(Target(), _source, CancellationToken.None);

        // Act
        var reused = await _images.ReusableAsync(
            Target([previous]),
            "state",
            _source.InputHash,
            CancellationToken.None
        );

        // Assert
        reused.Should().BeSameAs(previous);
    }

    [Fact]
    public async Task AChangedInputHashIsNotReused()
    {
        // Arrange
        var previous = await _images.BuildAsync(Target(), _source, CancellationToken.None);
        _root.File("state/files/new.txt", "changed\n");
        var changed = new ComponentSource(
            _source.Name,
            _source.Kind,
            _source.Directory,
            _source.Descriptor,
            _source.Arguments
        );

        // Act
        var reused = await _images.ReusableAsync(
            Target([previous]),
            "state",
            changed.InputHash,
            CancellationToken.None
        );

        // Assert
        changed.InputHash.Should().NotBe(_source.InputHash);
        reused.Should().BeNull();
    }

    [Fact]
    public async Task AComponentWhoseImageWasRemovedOrReplacedIsNotReused()
    {
        // Arrange
        var previous = await _images.BuildAsync(Target(), _source, CancellationToken.None);
        _store
            .FindImageIdAsync("host", previous.Reference, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Act
        var reused = await _images.ReusableAsync(
            Target([previous]),
            "state",
            _source.InputHash,
            CancellationToken.None
        );

        // Assert
        reused.Should().BeNull();
    }

    [Fact]
    public async Task ARebuildReusesNothing()
    {
        // Arrange
        var previous = await _images.BuildAsync(Target(), _source, CancellationToken.None);

        // Act
        var reused = await _images.ReusableAsync(
            Target([previous], rebuild: true),
            "state",
            _source.InputHash,
            CancellationToken.None
        );

        // Assert
        reused.Should().BeNull();
    }
}
