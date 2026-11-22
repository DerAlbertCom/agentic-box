using Abox.Features.Prune;
using Abox.Features.Shared.SandboxCleanup;
using NSubstitute;

namespace Abox.Tests;

public sealed class PruneServiceTests
{
    private const string Assembled = "docker.io/library/sandbox-kit-assembled";
    private const string KitSource = "docker.io/library/sbx-kit-src";

    private readonly TestConsole _console = new();
    private readonly ISandboxCleanup _cleanup = Substitute.For<ISandboxCleanup>();
    private readonly PruneService _service;

    public PruneServiceTests()
    {
        _service = new PruneService(_cleanup, _console);
        _cleanup
            .RemoveTemplateAsync(Arg.Any<TemplateImage>(), Arg.Any<CancellationToken>())
            .Returns(TemplateRemoval.Done);
        _cleanup.StopSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task NothingToRemoveIsReportedAndReturnsZero()
    {
        // Arrange
        _cleanup.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns([]);

        // Act
        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(false, false),
            CancellationToken.None
        );

        // Assert
        code.Should().Be(0);
        await _cleanup
            .DidNotReceive()
            .RemoveTemplateAsync(Arg.Any<TemplateImage>(), Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("Nothing to prune");
    }

    [Fact]
    public async Task TheStaleImagesAreRemovedAndThePlanDecidesIt()
    {
        // Arrange
        _cleanup.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());

        // Act
        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(false, false),
            CancellationToken.None
        );

        // Assert
        code.Should().Be(0);
        await _cleanup
            .Received()
            .RemoveTemplateAsync(
                Reference($"{Assembled}:0307199587cd420e-kit-1"),
                Arg.Any<CancellationToken>()
            );
        await _cleanup
            .Received()
            .RemoveTemplateAsync(
                Reference($"{KitSource}:hello-36b64f31e3e9"),
                Arg.Any<CancellationToken>()
            );
        await _cleanup
            .DidNotReceive()
            .RemoveTemplateAsync(
                Reference($"{Assembled}:7b7fd6a50dd39472-kit-2"),
                Arg.Any<CancellationToken>()
            );
        await _cleanup
            .DidNotReceive()
            .RemoveTemplateAsync(
                Reference($"{KitSource}:pi-config-eacbe0b12b58"),
                Arg.Any<CancellationToken>()
            );
        _console.AllText.Should().Contain("Removing 15 stale template images");
    }

    [Fact]
    public async Task AFailedRemovalStopsTheRunWithAnError()
    {
        // Arrange
        _cleanup.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _cleanup
            .RemoveTemplateAsync(
                Reference($"{Assembled}:3e09f411a663a519"),
                Arg.Any<CancellationToken>()
            )
            .Returns(new TemplateRemoval(false, "no such image"));

        // Act
        var act = () =>
            _service.PruneTemplatesAsync(new PruneRequest(false, false), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AboxException>().WithMessage("*3e09f411a663a519*");
    }

    [Fact]
    public async Task AFullPruneReachesForEverythingAndKeepsWhatSbxNeeds()
    {
        // Arrange
        _cleanup.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _cleanup
            .RemoveTemplateAsync(Arg.Any<TemplateImage>(), Arg.Any<CancellationToken>())
            .Returns(new TemplateRemoval(false, "the image is used by a sandbox"));

        // Act
        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(true, false),
            CancellationToken.None
        );

        // Assert
        code.Should().Be(0);
        await _cleanup
            .Received()
            .RemoveTemplateAsync(
                Reference($"{Assembled}:7b7fd6a50dd39472-kit-2"),
                Arg.Any<CancellationToken>()
            );
        await _cleanup
            .Received()
            .RemoveTemplateAsync(
                Reference($"{KitSource}:state-4403f5477cee"),
                Arg.Any<CancellationToken>()
            );
        _console.AllText.Should().Contain("sbx keeps");
    }

    [Fact]
    public async Task AFullPruneStopsTheSandboxesWhenItIsAskedTo()
    {
        // Arrange
        _cleanup.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _cleanup
            .ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(
                new List<SandboxSummary> { new("a-running-one", true), new("a-stopped-one", false) }
            );

        // Act
        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(true, true),
            CancellationToken.None
        );

        // Assert
        code.Should().Be(0);
        await _cleanup
            .DidNotReceive()
            .StopSandboxAsync("a-stopped-one", Arg.Any<CancellationToken>());
        await _cleanup.Received().StopSandboxAsync("a-running-one", Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("Stopping 1 running sandbox");
    }

    [Fact]
    public async Task ARegularPruneStopsTheSandboxesWhenItIsAskedTo()
    {
        // Arrange
        _cleanup.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _cleanup
            .ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SandboxSummary> { new("a-running-one", true) });

        // Act
        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(false, true),
            CancellationToken.None
        );

        // Assert
        code.Should().Be(0);
        await _cleanup.Received().StopSandboxAsync("a-running-one", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalRefusalDoesNotPreventLaterIndividualRemovals(bool full)
    {
        // Arrange
        _cleanup
            .ListTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns([
                SourceImage("refused", "refused", "2026-11-01T00:00:00Z"),
                SourceImage("removed", "removed", "2026-11-01T00:00:00Z"),
            ]);
        _cleanup
            .RemoveTemplateAsync(Reference($"{KitSource}:refused"), Arg.Any<CancellationToken>())
            .Returns(new TemplateRemoval(false, "synthetic in-use refusal"));

        // Act
        var act = () =>
            _service.PruneTemplatesAsync(new PruneRequest(full, false), CancellationToken.None);

        // Assert
        if (full)
            (await act()).Should().Be(0);
        else
            await act.Should().ThrowAsync<AboxException>().WithMessage($"*{KitSource}:refused*");
        await _cleanup
            .Received(1)
            .RemoveTemplateAsync(Reference($"{KitSource}:removed"), Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain($"Removed {KitSource}:removed.");
        _console
            .AllText.Should()
            .Contain(full ? $"sbx keeps {KitSource}:refused" : "synthetic in-use refusal");
    }

    private static TemplateImage Reference(string reference) =>
        Arg.Is<TemplateImage>(image => image.Reference == reference);

    /// <summary>One image of an assembled set, backdated conveniently.</summary>
    private static TemplateImage SetImage(string set, string tag, string id, string created) =>
        new(
            $"{Assembled}:{tag}",
            TemplateKind.AssembledSet,
            set,
            id,
            DateTimeOffset.Parse(created)
        );

    /// <summary>One kit source image, backdated conveniently.</summary>
    private static TemplateImage SourceImage(string tag, string id, string created) =>
        new(
            $"{KitSource}:{tag}",
            TemplateKind.KitSource,
            string.Empty,
            id,
            DateTimeOffset.Parse(created)
        );

    /// <summary>The template images of one real afternoon, in the order sbx lists them.</summary>
    private static List<TemplateImage> SampleImages()
    {
        var images = new List<TemplateImage>();
        images.AddRange(
            SetImage("0307199587cd420e", "0307199587cd420e", "527ef0280e47", "2026-11-08T14:49:51Z")
        );
        images.AddRange(
            SetImage(
                "0307199587cd420e",
                "0307199587cd420e-kit-0",
                "64bfab03fc89",
                "2026-11-08T14:49:51Z"
            )
        );
        images.AddRange(
            SetImage(
                "0307199587cd420e",
                "0307199587cd420e-kit-1",
                "ecc48f9d23bd",
                "2026-11-08T14:55:39Z"
            )
        );
        images.AddRange(
            SetImage(
                "0307199587cd420e",
                "0307199587cd420e-template",
                "64bfab03fc89",
                "2026-11-08T14:49:51Z"
            )
        );
        images.AddRange(
            SetImage("26cb35b823b12331", "26cb35b823b12331", "b1f867cc5302", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            SetImage(
                "26cb35b823b12331",
                "26cb35b823b12331-kit-0",
                "9ea13c4590bc",
                "2026-11-08T15:02:00Z"
            )
        );
        images.AddRange(
            SetImage(
                "26cb35b823b12331",
                "26cb35b823b12331-kit-1",
                "ecc48f9d23bd",
                "2026-11-08T14:55:39Z"
            )
        );
        images.AddRange(
            SetImage(
                "26cb35b823b12331",
                "26cb35b823b12331-template",
                "9ea13c4590bc",
                "2026-11-08T15:02:00Z"
            )
        );
        images.AddRange(
            SetImage("3e09f411a663a519", "3e09f411a663a519", "4ec245bdff7f", "2026-09-22T06:14:04Z")
        );
        images.AddRange(
            SetImage("7b7fd6a50dd39472", "7b7fd6a50dd39472", "884620d2ac71", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            SetImage(
                "7b7fd6a50dd39472",
                "7b7fd6a50dd39472-kit-0",
                "9ea13c4590bc",
                "2026-11-08T15:02:00Z"
            )
        );
        images.AddRange(
            SetImage(
                "7b7fd6a50dd39472",
                "7b7fd6a50dd39472-kit-1",
                "c82890ca8c9d",
                "2026-11-08T15:06:03Z"
            )
        );
        images.AddRange(
            SetImage(
                "7b7fd6a50dd39472",
                "7b7fd6a50dd39472-kit-2",
                "ecc48f9d23bd",
                "2026-11-08T14:55:39Z"
            )
        );
        images.AddRange(
            SetImage(
                "7b7fd6a50dd39472",
                "7b7fd6a50dd39472-template",
                "9ea13c4590bc",
                "2026-11-08T15:02:00Z"
            )
        );
        images.AddRange(
            SourceImage("elastic-auth-602ed3df56ec", "2a10892e8779", "2026-11-08T14:50:52Z")
        );
        images.AddRange(
            SourceImage(
                "github-enterprise-auth-784ba46cf335",
                "c2b4c5c6abce",
                "2026-11-08T14:50:51Z"
            )
        );
        images.AddRange(SourceImage("hello-36b64f31e3e9", "8046dc73a417", "2026-11-08T14:27:39Z"));
        images.AddRange(
            SourceImage("opencode-config-ec5837b345f0", "003b3fbe5ffd", "2026-11-08T14:50:51Z")
        );
        images.AddRange(
            SourceImage("pi-config-eacbe0b12b58", "ecc48f9d23bd", "2026-11-08T14:55:39Z")
        );
        images.AddRange(SourceImage("state-4403f5477cee", "c82890ca8c9d", "2026-11-08T15:06:03Z"));
        images.AddRange(SourceImage("state-a6d3285689a3", "b0542b79d94d", "2026-11-08T14:50:52Z"));
        images.AddRange(
            SourceImage("workload-21a9f1cddc8a", "9ea13c4590bc", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            SourceImage("workload-49a52a22791b", "64bfab03fc89", "2026-11-08T14:49:51Z")
        );
        return images;
    }
}
