using System.Text.Json;
using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildServiceTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private IProcessRunner Runner => _fixture.Runner;
    private TestConsole Console => _fixture.Console;
    private BuildStore Store => _fixture.Store;
    private SquadResolver Resolver => _fixture.Resolver;
    private BuildService Service => _fixture.Service;

    [Fact]
    public async Task EachBuildKeepsTheRepositoryOfItsInheritedWorkload()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "base: cape\n");
        Root.File("teams/other/cape.abox", "workload: shared\n");
        Root.File("kits/shared.workload", "publishRepository: shared-images-local\n");
        Root.File("teams/sample/bd.abox", "workload: sample\n");
        Root.File("teams/sample/sample.workload", "publishRepository: sample-images-local\n");

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        Store
            .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            .PublishRepository.Should()
            .Be("shared-images-local");
        Store
            .Load(new SquadBuildIdentity("sample", "bd", "latest"))!
            .PublishRepository.Should()
            .Be("sample-images-local");
    }

    [Fact]
    public async Task ChangingTheWorkloadRepositoryRequiresRebuildingBeforePublication()
    {
        // Arrange
        Root.File("teams/sample/sample.workload", "publishRepository: original-images-local\n");
        await Service.BuildAsync(Request(), CancellationToken.None);
        Root.File("teams/sample/sample.workload", "publishRepository: other-images-local\n");

        // Act
        var check = () =>
            BuildSources.EnsureUnchanged(
                Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!
            );

        // Assert
        check.Should().Throw<AboxException>().WithMessage("*sources changed*");
    }

    [Fact]
    public async Task TheCertificateAssetsAreFingerprinted()
    {
        // Arrange: the fixture supplies a publishable workload and bundled assets.
        await Service.BuildAsync(Request(), CancellationToken.None);
        var manifest = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var asset = Path.Combine("certificates", "rwe-private-root-ca-r1.crt");

        File.AppendAllText(Path.Combine(manifest.WorkloadKitDirectory, asset), "\n# changed\n");
        // Act
        var check = () => BuildSources.EnsureUnchanged(manifest);
        // Assert
        check.Should().Throw<AboxException>().WithMessage("*sources changed*");
    }

    private BuildRequest Request() =>
        new([Resolver.ResolveTeam(Root.Path, "sample")], Root.Path, "latest");

    private JsonElement BundledSquadMetadata()
    {
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var json = File.ReadAllText(Path.Combine(build.ConfigurationMixinDirectory!, "squad.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static void AssertJson(JsonElement actual, string expectedJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        JsonElement
            .DeepEquals(actual, expected.RootElement)
            .Should()
            .BeTrue("expected {0}, but found {1}", expectedJson, actual.GetRawText());
    }

    private const string Components =
        "rwest.jfrog.io/source-images-local/agentic-box-rwest-sample/ac/components/";

    private BuildManifest Record() => Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;

    /// <summary>The names of the component images the synthetic Docker built, in order.</summary>
    private List<string> BuiltComponents() =>
        Runner
            .ReceivedCalls()
            .Select(call => (ProcessRequest)call.GetArguments()[0]!)
            .Where(request => request.Arguments.Contains("--load"))
            .Select(request => request.Arguments[request.Arguments.ToList().IndexOf("--tag") + 1])
            .Select(reference => reference[Components.Length..reference.LastIndexOf(':')])
            .ToList();

    private void SquadWithEveryComponent() =>
        Root.File(
            "teams/sample/ac.abox",
            "workload: sample\nkits: [private, ghcr.io/example/remote:1]\nstate: [~/.probe-state]\n"
        );

    private void ResolveTo(string reference, string digest) =>
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("{{json .Manifest}}")
                    && request.Arguments.Last() == reference
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, JsonSerializer.Serialize(new { digest }), ""));

    [Fact]
    public async Task TheBuildRecordListsEveryComponentImageUnderItsRegistryNameWithItsDigest()
    {
        // Arrange
        SquadWithEveryComponent();

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var record = Record();
        record
            .Components.Select(component => component.Name)
            .Should()
            .Equal("workload", "kits/private", "state", "config");
        foreach (var component in record.Components)
        {
            component.Reference.Should().Be($"{Components}{component.Name}:{component.InputHash}");
            component.Digest.Should().Be(LocalWorkloadFactory.Digest(component.Reference));
            component.ImageId.Should().Be(LocalWorkloadFactory.ImageId);
            component.Descriptor.Should().Contain("kind:");
        }
        record.Components[0].Kind.Should().Be(ComponentKind.Workload);
        record.Components.Skip(1).Should().OnlyContain(component => component.Kind == "mixin");
        BuiltComponents().Should().Equal("workload", "kits/private", "state", "config");
        record.ImageStore.Should().Be("test-context");
        record.SourceHashes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TheBaseImageAndTheRegistryKitsArePinnedToTheDigestTheyResolveTo()
    {
        // Arrange
        SquadWithEveryComponent();

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var record = Record();
        var baseDigest = LocalWorkloadFactory.Digest("example.com/base:1");
        record.Base!.Reference.Should().Be("example.com/base:1");
        record.Base.Digest.Should().Be(baseDigest);
        File.ReadAllText(Path.Combine(record.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain($"FROM example.com/base@{baseDigest}");
        var remote = record.Kits.Single(kit => !kit.IsLocal);
        remote.Digest.Should().Be(LocalWorkloadFactory.Digest("ghcr.io/example/remote:1"));
        remote.Component.Should().BeNull();
        record.Kits.Single(kit => kit.IsLocal).Component.Should().Be("kits/private");
    }

    [Fact]
    public async Task ASecondBuildWithoutChangesBuildsNothingAndKeepsItsCreationTime()
    {
        // Arrange
        SquadWithEveryComponent();
        await Service.BuildAsync(Request(), CancellationToken.None);
        var first = Record();
        Runner.ClearReceivedCalls();

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        BuiltComponents().Should().BeEmpty();
        var second = Record();
        second.CreatedAt.Should().Be(first.CreatedAt);
        second
            .Components.Select(component => component.Digest)
            .Should()
            .Equal(first.Components.Select(component => component.Digest));
    }

    [Fact]
    public async Task ChangingOneLocalKitRebuildsOnlyThatKitAndTheConfigurationThatDatesTheBuild()
    {
        // Arrange
        SquadWithEveryComponent();
        await Service.BuildAsync(Request(), CancellationToken.None);
        var first = Record();
        Runner.ClearReceivedCalls();
        Root.File("kits/private/files/changed.txt", "changed\n");

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        BuiltComponents().Should().Equal("kits/private", "config");
        Record().CreatedAt.Should().BeAfter(first.CreatedAt);
    }

    [Fact]
    public async Task ChangingOnlyTheSquadMetadataRebuildsOnlyTheConfiguration()
    {
        // Arrange
        await Service.BuildAsync(Request(), CancellationToken.None);
        Runner.ClearReceivedCalls();
        Root.File("teams/sample/ac.abox", "workload: sample\nmcp: [elastic]\n");

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        BuiltComponents().Should().Equal("config");
    }

    [Fact]
    public async Task ARebuildBuildsEveryComponentAgain()
    {
        // Arrange
        SquadWithEveryComponent();
        await Service.BuildAsync(Request(), CancellationToken.None);
        Runner.ClearReceivedCalls();

        // Act
        await Service.BuildAsync(Request() with { Rebuild = true }, CancellationToken.None);

        // Assert
        BuiltComponents().Should().Equal("workload", "kits/private", "state", "config");
    }

    [Fact]
    public async Task AComponentImageThatWasRemovedFromTheImageStoreIsBuiltAgain()
    {
        // Arrange
        await Service.BuildAsync(Request(), CancellationToken.None);
        Runner.ClearReceivedCalls();
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("{{.Id}}")
                    && request.Arguments.Last().Contains("/components/workload:")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProcessResult(1, "", "No such image"),
                new ProcessResult(0, LocalWorkloadFactory.ImageId, "")
            );

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        BuiltComponents().Should().Equal("workload", "config");
    }

    [Fact]
    public async Task AMovedBaseImageDigestRebuildsTheWorkload()
    {
        // Arrange
        await Service.BuildAsync(Request(), CancellationToken.None);
        Runner.ClearReceivedCalls();
        var moved = "sha256:" + new string('b', 64);
        ResolveTo("example.com/base:1", moved);

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        BuiltComponents().Should().Equal("workload", "config");
        Record().Base!.Digest.Should().Be(moved);
    }

    [Fact]
    public async Task AMovedRegistryKitDigestIsPinnedAgainAndDatesTheBuild()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [ghcr.io/example/remote:1]\n");
        await Service.BuildAsync(Request(), CancellationToken.None);
        var first = Record();
        Runner.ClearReceivedCalls();
        var moved = "sha256:" + new string('c', 64);
        ResolveTo("ghcr.io/example/remote:1", moved);

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        BuiltComponents().Should().Equal("config");
        var second = Record();
        second.Kits.Single().Digest.Should().Be(moved);
        second.CreatedAt.Should().BeAfter(first.CreatedAt);
    }

    [Fact]
    public async Task ABuildPhaseArgumentOfALocalKitIsRejected()
    {
        // Arrange
        Root.File(
            "teams/sample/ac.abox",
            "workload: sample\nkits:\n  - name: private\n    args: {version: \"1\"}\n"
        );
        Root.File(
            "kits/private/private.yaml",
            "schemaVersion: \"3\"\nkind: mixin\nargs:\n  version:\n    buildArg: VERSION\n"
        );

        // Act
        var action = () => Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "The argument 'version' of kit 'private' is undeclared or build-phase.*"
        );
    }

    [Fact]
    public async Task SourcesChangedDuringAnImageBuildAreNotSaved()
    {
        // Arrange
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--load")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                File.AppendAllText(
                    Path.Combine(
                        Store.DirectoryFor(new SquadBuildIdentity("sample", "ac", "latest")),
                        "workload",
                        "workload.yaml"
                    ),
                    "description: changed during the build\n"
                );
                return LocalWorkloadFactory.Result(call.Arg<ProcessRequest>());
            });

        // Act
        var action = () => Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*changed during*");
        Store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
    }

    [Fact]
    public async Task AWorkloadWithoutPublishRepositoryCannotNameItsImages()
    {
        // Arrange
        Root.File("teams/sample/sample.workload", "template: example.com/base:1\n");

        // Act
        var action = () => Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "The workload 'sample' has no valid publishRepository*"
        );
    }
}
