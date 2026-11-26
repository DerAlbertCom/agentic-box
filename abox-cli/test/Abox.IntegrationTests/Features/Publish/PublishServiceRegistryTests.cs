using System.Text.Json;
using Abox.Features.Publish;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Registry;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceRegistryTests
{
    private static readonly string Pushed = "sha256:" + new string('1', 64);
    private static readonly string Leaf = "sha256:" + new string('2', 64);
    private readonly PublishServiceFixture _fixture = new();
    private readonly IRegistryPublisher _registry = Substitute.For<IRegistryPublisher>();
    private readonly PublishService _service;

    public PublishServiceRegistryTests()
    {
        _service = new PublishService(
            _fixture.Store,
            _registry,
            new DockerRegistryDigests(LocalWorkloadFactory.Runner()),
            LocalWorkloadFactory.Images(),
            _fixture.Console
        );
        _registry.IsInstalled.Returns(true);
        _registry.PushAsync(Arg.Any<RegistryPush>(), Arg.Any<CancellationToken>()).Returns(Pushed);
        _registry
            .InspectAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Manifest(call.ArgAt<string>(0)));
        _fixture.SaveBuild("ac");
    }

    private static RegistryManifest Manifest(string reference) =>
        reference.EndsWith(Pushed, StringComparison.Ordinal)
            ? new RegistryManifest(
                null,
                [
                    new PlatformManifest("not-a-digest", "unknown", "unknown", IsAttestation: true),
                    new PlatformManifest(Leaf, "linux", "amd64"),
                ]
            )
            : new RegistryManifest(
                JsonSerializer.SerializeToElement(
                    new
                    {
                        schemaVersion = "3",
                        kind = reference.Contains("/components/config") ? "mixin" : "workload",
                    }
                ),
                null
            );

    [Fact]
    public async Task MissingImageToolingStopsPublicationBeforeAnyRegistryOperation()
    {
        // Arrange
        _registry.IsInstalled.Returns(false);

        // Act
        var action = () => _service.PublishAsync(_fixture.Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Publishing v3 kits needs Docker with the Buildx plugin on PATH.*"
        );
        await _registry.DidNotReceive().RequireReadyAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttestationsAreSkippedAndEveryPlatformImageIsVerifiedBeforePromotion()
    {
        // Act
        await _service.PublishAsync(
            _fixture.Request("ac") with
            {
                Builder = "publisher",
            },
            CancellationToken.None
        );

        // Assert
        await _registry
            .DidNotReceive()
            .InspectAsync(
                Arg.Is<string>(reference => reference.Contains("not-a-digest")),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await _registry
            .Received(1)
            .InspectAsync(
                Arg.Is<string>(reference => reference.EndsWith(Leaf, StringComparison.Ordinal)),
                "publisher",
                Arg.Any<CancellationToken>()
            );
        await _registry
            .Received(1)
            .TagAsync(
                Arg.Is<string>(candidate => candidate.EndsWith(Pushed, StringComparison.Ordinal)),
                Arg.Is<string>(reference =>
                    reference.EndsWith(":latest", StringComparison.Ordinal)
                ),
                "publisher",
                Arg.Any<CancellationToken>()
            );
        _fixture.Console.AllText.Should().Contain("Published squad 'ac'");
    }

    [Fact]
    public async Task APushWithoutAReadableDigestIsNeverPromoted()
    {
        // Arrange
        _registry
            .PushAsync(Arg.Any<RegistryPush>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        // Act
        var action = () => _service.PublishAsync(_fixture.Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Cannot read the pushed digest for '*'; the requested set tag was not promoted."
        );
        await _registry
            .DidNotReceive()
            .TagAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }
}
