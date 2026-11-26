using System.Text.Json;
using Abox.Features.Publish;
using Abox.Features.Shared.Common;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceOciTests
{
    private readonly PublishServiceFixture _fixture = new();
    private IProcessRunner Runner => _fixture.Runner;
    private TestConsole Console => _fixture.Console;
    private List<ProcessRequest> Calls => _fixture.Calls;
    private PublishService Service => _fixture.Service;

    private PublishRequest Request(params string[] squads) => _fixture.Request(squads);

    private void SaveBuild(string squad) => _fixture.SaveBuild(squad);

    private const string VerifiedDigest =
        "sha256:5555555555555555555555555555555555555555555555555555555555555555";

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":2,"annotations":42}""")]
    [InlineData(
        """{"schemaVersion":2,"annotations":{"vnd.docker.sandbox.kit.descriptor":"not-json"}}"""
    )]
    [InlineData(
        """{"schemaVersion":2,"annotations":{"vnd.docker.sandbox.kit.descriptor":"{\"schemaVersion\":\"2\",\"kind\":\"mixin\"}"}}"""
    )]
    [InlineData(
        """{"schemaVersion":2,"annotations":{"vnd.docker.sandbox.kit.descriptor":"{\"schemaVersion\":\"3\",\"kind\":\"workload\"}"}}"""
    )]
    [InlineData("""{"schemaVersion":2,"manifests":[]}""")]
    [InlineData("""{"schemaVersion":2,"manifests":[{"annotations":42}]}""")]
    [InlineData("""{"schemaVersion":2,"manifests":[{"digest":"sha256:abc"}]}""")]
    public async Task InvalidRegistryKitsStopPublicationBeforeAnyPush(string manifest)
    {
        // Arrange
        SaveRegistryKitBuild();
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, manifest, ""));

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 mixin descriptor*Publication stopped*"
        );
        Calls.Should().NotContain(call => call.Arguments.Contains("--push"));
        Calls.Should().NotContain(call => call.Arguments.Contains("create"));
        Console.AllText.Should().NotContain("Published squad");
    }

    [Fact]
    public async Task ExcessivelyNestedRegistryKitsStopPublicationWithoutPromotingATag()
    {
        // Arrange
        SaveRegistryKitBuild();
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, PublicationIndex(VerifiedDigest), ""));

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*has too many nested image indexes*"
        );
        Calls.Should().NotContain(call => call.Arguments.Contains("--push"));
        Calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }

    [Fact]
    public async Task AnInvalidPlatformKitPreventsPublicationEvenWhenAnotherPlatformIsValid()
    {
        // Arrange
        SaveRegistryKitBuild();
        const string invalidDigest =
            "sha256:6666666666666666666666666666666666666666666666666666666666666666";
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var reference = call.Arg<ProcessRequest>().Arguments.Last();
                var manifest =
                    reference.EndsWith(VerifiedDigest, StringComparison.Ordinal)
                        ? PublishTestProcess.Manifest("mixin")
                    : reference.EndsWith(invalidDigest, StringComparison.Ordinal)
                        ? PublishTestProcess.Manifest("workload")
                    : PublicationIndex(VerifiedDigest, invalidDigest);
                return new ProcessResult(0, manifest, "");
            });

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 mixin descriptor*"
        );
        Calls
            .Should()
            .Contain(call => call.Arguments.Last() == "ghcr.io/example/tool@" + invalidDigest);
        Calls.Should().NotContain(call => call.Arguments.Contains("--push"));
        Calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }

    [Fact]
    public async Task NestedRegistryKitsUseThePublicationBuilderAndPermitAVerifiedSet()
    {
        // Arrange
        SaveRegistryKitBuild();
        const string leafDigest =
            "sha256:7777777777777777777777777777777777777777777777777777777777777777";
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call =>
                    call.Arguments.Contains("--raw") && call.Arguments.Last().StartsWith("ghcr.io/")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var reference = call.Arg<ProcessRequest>().Arguments.Last();
                var manifest = reference.EndsWith(leafDigest, StringComparison.Ordinal)
                    ? PublishTestProcess.Manifest("mixin")
                    : PublicationIndex(
                        reference.EndsWith(VerifiedDigest, StringComparison.Ordinal)
                            ? leafDigest
                            : VerifiedDigest
                    );
                return new ProcessResult(0, manifest, "");
            });

        // Act
        var result = await Service.PublishAsync(
            Request("ac") with
            {
                Builder = "publisher",
            },
            CancellationToken.None
        );

        // Assert
        result.Should().Be(0);
        Calls
            .Where(call => call.Arguments.Contains("--raw"))
            .Should()
            .OnlyContain(call =>
                call.Arguments.Contains("--builder") && call.Arguments.Contains("publisher")
            );
        Calls
            .Should()
            .Contain(call => call.Arguments.Last() == "ghcr.io/example/tool@" + leafDigest);
        Calls.Should().ContainSingle(call => call.Arguments.Contains("create"));
        Console.AllText.Should().Contain("Published squad 'ac'");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("""{"containerimage.digest":"sha256:abc"}""")]
    [InlineData("""{"containerimage.digest":42}""")]
    public async Task MissingOrInvalidPushedDigestsNeverReachPromotion(string metadata)
    {
        // Arrange
        SaveBuild("ac");
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var args = call.Arg<ProcessRequest>().Arguments.ToList();
                File.WriteAllText(args[args.IndexOf("--metadata-file") + 1], metadata);
                return new ProcessResult(0, "", "");
            });

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Cannot read the pushed digest*requested set tag was not promoted*"
        );
        Calls.Should().NotContain(call => call.Arguments.Contains("create"));
        Console.AllText.Should().NotContain("Published squad");
    }

    [Fact]
    public async Task PublicationInspectsPushedDigestsAndPromotesOnlyTheImmutableCandidate()
    {
        // Arrange
        SaveBuild("ac");
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var args = call.Arg<ProcessRequest>().Arguments.ToList();
                File.WriteAllText(
                    args[args.IndexOf("--metadata-file") + 1],
                    JsonSerializer.Serialize(
                        new Dictionary<string, string>
                        {
                            ["containerimage.digest"] = VerifiedDigest,
                        }
                    )
                );
                return new ProcessResult(0, "", "");
            });

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        const string repository =
            "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac";
        var inspected = Calls
            .Where(call => call.Arguments.Contains("--raw"))
            .Select(call => call.Arguments.Last());
        inspected.Should().Contain(repository + "/components/workload@" + VerifiedDigest);
        inspected.Should().Contain(repository + "/components/config@" + VerifiedDigest);
        inspected.Should().Contain(repository + "@" + VerifiedDigest);
        var promotion = Calls.Single(call => call.Arguments.Contains("create"));
        promotion.Arguments.Should().Contain("--prefer-index=false");
        promotion.Arguments.Should().Contain(repository + ":latest");
        promotion.Arguments.Last().Should().Be(repository + "@" + VerifiedDigest);
        Calls.Last().Arguments.Should().Contain(repository + ":latest");
    }

    private static string PublicationIndex(params string[] digests) =>
        JsonSerializer.Serialize(
            new { schemaVersion = 2, manifests = digests.Select(digest => new { digest }) }
        );

    private void SaveRegistryKitBuild() => _fixture.SaveRegistryKitBuild();
}
