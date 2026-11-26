using System.Text.Json;
using Abox.Features.Publish;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private BuildStore Store => _fixture.Store;
    private IProcessRunner Runner => _fixture.Runner;
    private IHostPrograms Programs => _fixture.Programs;
    private TestConsole Console => _fixture.Console;
    private List<ProcessRequest> Calls => _fixture.Calls;
    private List<string> Descriptors => _fixture.Descriptors;
    private List<string> Configurations => _fixture.Configurations;
    private PublishService Service => _fixture.Service;

    private void SaveRegistryKitBuild() => _fixture.SaveRegistryKitBuild();

    [Fact]
    public async Task LocalAndRegistryKitsKeepTheirArgumentsAndStateIsIncluded()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var local = Path.Combine(Root.Path, "local kit");
        Directory.CreateDirectory(local);
        File.WriteAllText(
            Path.Combine(local, "spec.yaml"),
            "schemaVersion: \"3\"\nkind: mixin\nargs:\n  url:\n    env: SERVICE_URL\n"
        );
        build.Kits =
        [
            new ManifestKit
            {
                Name = "local",
                Reference = local,
                IsLocal = true,
                Arguments = new() { ["url"] = "https://example.com/a?x=hello world&y=1" },
            },
            new ManifestKit
            {
                Name = "remote",
                Reference = "ghcr.io/example/tool:1",
                Arguments = new() { ["message"] = "quoted: \"hello\"\nnext line" },
            },
        ];
        build.StateMixinDirectory = local;
        build.State.Add(
            new ManifestState { SandboxPath = "/home/agent/.state", RelativePath = ".state" }
        );
        build.Mcp.Add(new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" });
        SaveVerifiedBuild(build);

        // Act
        await Service.PublishAsync(
            Request("ac") with
            {
                Builder = "custom-builder",
            },
            CancellationToken.None
        );

        // Assert
        var pushes = Calls.Where(call => call.Arguments.Contains("--push")).ToList();
        pushes.Should().HaveCount(5);
        pushes.Should().OnlyContain(call => call.Arguments.Contains("custom-builder"));
        pushes.Should().OnlyContain(call => !call.Arguments.Contains("--build-arg"));
        var set = new YamlDotNetParser().Read(Descriptors.Last(), "set");
        var kits = set.GetEntries("kits");
        kits.Select(kit => kit.GetString("ref")!.Split('@')[0])
            .Should()
            .Equal(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/workload",
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/kit-0",
                "ghcr.io/example/tool:1",
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/state",
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/config"
            );
        kits[1].GetEntry("args")!.GetString("url").Should().Be(build.Kits[0].Arguments["url"]);
        kits[2]
            .GetEntry("args")!
            .GetString("message")
            .Should()
            .Be(build.Kits[1].Arguments["message"]);
        kits[3].GetEntry("args").Should().BeNull();
        using var config = JsonDocument.Parse(Configurations.Single());
        config.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(1);
        AssertBundledRuntimeMetadata(config.RootElement);
        Configurations.Single().Should().NotContain(Root.Path);
    }

    private static void AssertBundledRuntimeMetadata(JsonElement metadata)
    {
        AssertPublicJson(
            metadata.GetProperty("Mcp"),
            """
            [{"Name": "elastic", "Url": "https://example.com/mcp"}]
            """
        );
        AssertPublicJson(
            metadata.GetProperty("State"),
            """
            [{"SandboxPath": "/home/agent/.state", "RelativePath": ".state"}]
            """
        );
        AssertPublicJson(
            metadata.GetProperty("Kits"),
            """
            [
              {"Name": "local", "Arguments": {"url": "https://example.com/a?x=hello world&y=1"}},
              {"Name": "remote", "Arguments": {"message": "quoted: \"hello\"\nnext line"}}
            ]
            """
        );
    }

    private static void AssertPublicJson(JsonElement actual, string expectedJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        JsonElement
            .DeepEquals(actual, expected.RootElement)
            .Should()
            .BeTrue(
                "expected public metadata {0}, but found {1}",
                expectedJson,
                actual.GetRawText()
            );
    }

    [Fact]
    public async Task EachSquadPublishesToItsOwnWorkloadRepository()
    {
        // Arrange
        SaveBuild("ac", "first-images-local");
        SaveBuild("bd", "second-images-local");

        // Act
        await Service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        var tags = Calls
            .Where(call => call.Arguments.Contains("--tag"))
            .Select(call => call.Arguments[call.Arguments.ToList().IndexOf("--tag") + 1])
            .ToList();
        tags.Should().HaveCount(8);
        tags.Take(4)
            .Should()
            .OnlyContain(tag =>
                tag.StartsWith("rwest.jfrog.io/first-images-local/agentic-box-rwest-sample/ac")
            );
        tags.Skip(4)
            .Should()
            .OnlyContain(tag =>
                tag.StartsWith("rwest.jfrog.io/second-images-local/agentic-box-rwest-sample/bd")
            );
        tags.Should()
            .Contain("rwest.jfrog.io/first-images-local/agentic-box-rwest-sample/ac:latest");
        tags.Should()
            .Contain("rwest.jfrog.io/second-images-local/agentic-box-rwest-sample/bd:latest");
        foreach (var descriptor in Descriptors.Where(text => text.Contains("\"kind\": \"set\"")))
        {
            descriptor.Should().NotContain("mtiae-developer-container-images-local");
        }
    }

    [Fact]
    public async Task MultipleSquadsDoNotOverwriteEachOther()
    {
        // Arrange
        SaveBuild("ac");
        SaveBuild("bd");

        // Act
        await Service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        Calls
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest"
            );
        Calls
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/bd:latest"
            );
        Calls
            .SelectMany(call => call.Arguments)
            .Should()
            .NotContain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample:latest"
            );
    }

    [Fact]
    public async Task LegacyBuildsWithoutAPublishRepositoryMustBeRebuilt()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.PublishRepository = null;
        Store.Save(build);

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*publishRepository*.workload*abox build --team sample --tag latest*"
        );
        Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../other")]
    [InlineData("team/repository")]
    [InlineData("repository:tag")]
    [InlineData("rwest.jfrog.io/repository")]
    [InlineData("Repository")]
    [InlineData("repository\n")]
    public async Task EveryBuildRepositoryIsValidatedBeforeAnyDockerCall(string repository)
    {
        // Arrange
        SaveBuild("ac");
        SaveBuild("bd", repository);

        // Act
        var action = () => Service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*publishRepository*");
        Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("elastic,other", null)]
    [InlineData("elastic", "file:///local")]
    [InlineData("elastic", "https://user:password@example.com/mcp")]
    public async Task EveryBuildIsCheckedBeforeAnythingIsPublished(string? name, string? url)
    {
        // Arrange
        SaveBuild("ac");
        if (name is not null)
        {
            SaveBuild("bd");
            var build = Store.Load(new SquadBuildIdentity("sample", "bd", "latest"))!;
            build.Mcp.Add(new ManifestMcp { Name = name, Url = url });
            Store.Save(build);
        }

        // Act
        var action = () => Service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            name is null ? "*abox build --team sample --tag latest*" : "*MCP server*mcp.*not valid*"
        );
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ASquadBuildFromAnotherTeamIsNotPublished()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Reference = "agentic-box-rwest-other:latest";
        File.WriteAllText(
            ManifestPath(new("sample", "ac", "latest")),
            JsonSerializer.Serialize(build)
        );

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*no proven ownership for team 'sample'*abox build --team sample*"
        );
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingBuiltKitDoesNotStartAPublish()
    {
        // Arrange
        SaveBuild("ac");
        File.Delete(
            Path.Combine(
                Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.WorkloadKitDirectory,
                "workload.yaml"
            )
        );

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*has no descriptor*");
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingDockerGetsAnInstallHint()
    {
        // Arrange
        SaveBuild("ac");
        Programs.Exists("docker").Returns(false);

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Docker with the Buildx plugin*"
        );
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedPushDoesNotMoveTheSetTagAndGetsAnAuthenticationHint()
    {
        // Arrange
        SaveBuild("ac");
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(1, string.Empty, "unauthorized"));

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        var exception = (await action.Should().ThrowAsync<AboxException>()).Which;
        exception
            .Message.Should()
            .Contain("docker login rwest.jfrog.io")
            .And.Contain("unauthorized");
        await Runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            );
        Console.AllText.Should().NotContain("Published squad");
        Calls
            .Where(call => call.Arguments.Contains("--push"))
            .Should()
            .OnlyContain(call => !Directory.Exists(call.Arguments.Last()));
    }

    [Fact]
    public async Task ComponentTagsAreUniqueAcrossPublishes()
    {
        // Arrange
        SaveBuild("ac");
        await Service.PublishAsync(Request("ac"), CancellationToken.None);
        var firstSet = new YamlDotNetParser().Read(Descriptors.Last(), "first set");

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        var secondSet = new YamlDotNetParser().Read(Descriptors.Last(), "second set");
        firstSet
            .GetEntries("kits")[0]
            .GetString("ref")
            .Should()
            .NotBe(secondSet.GetEntries("kits")[0].GetString("ref"));
    }

    [Fact]
    public async Task TheChosenTagIsUsedForTheLocalBuildAndThePublishedSet()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Tag = "release-1";
        build.Reference = "agentic-box-rwest-sample:release-1";
        Store.Save(build);

        // Act
        await Service.PublishAsync(
            Request("ac") with
            {
                Tag = "release-1",
            },
            CancellationToken.None
        );

        // Assert
        Calls
            .Last()
            .Arguments.Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:release-1"
            );
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("tag:invalid")]
    [InlineData("")]
    public async Task InvalidRegistryTagsAreRejectedBeforeAccessingBuilds(string tag)
    {
        // Arrange: the fixture has no builds for the invalid tag.
        // Act
        var action = () =>
            Service.PublishAsync(Request("ac") with { Tag = tag }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*not a valid registry tag*"
        );
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheBuiltWorkloadIsPublishedAsAv3SetToJfrog()
    {
        // Arrange
        SaveBuild("ac");

        // Act
        var result = await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        result.Should().Be(0);
        var pushes = Calls.Where(call => call.Arguments.Contains("--push")).ToList();
        pushes.Should().HaveCount(3); // workload, portable squad configuration, set
        pushes.Should().OnlyContain(call => call.FileName == "docker");
        Calls
            .Single(call => call.Arguments.Contains("create"))
            .Arguments.Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest"
            );
        pushes.Should().OnlyContain(call => call.Arguments.Contains("--provenance=mode=min"));
        var set = new YamlDotNetParser().Read(Descriptors.Last(), "published set");
        set.GetString("kind").Should().Be("set");
        set.GetString("schemaVersion").Should().Be("3");
        set.GetEntries("kits").Should().HaveCount(2);
        Console.AllText.Should().Contain("Published");
    }

    private PublishRequest Request(params string[] squads) => _fixture.Request(squads);

    [Fact]
    public async Task PublishedSetOmitsOnlyAbsentComponentArguments()
    {
        // Arrange
        SaveRegistryKitBuild();

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        using var set = JsonDocument.Parse(Descriptors.Last().Split('\n', 2)[1]);
        var descriptor = set.RootElement;
        descriptor
            .EnumerateObject()
            .Select(field => field.Name)
            .Should()
            .Equal("schemaVersion", "kind", "displayName", "kits");
        descriptor.GetProperty("schemaVersion").GetString().Should().Be("3");
        descriptor.GetProperty("kind").GetString().Should().Be("set");
        descriptor.GetProperty("displayName").GetString().Should().Be("sample / ac");
        var components = descriptor.GetProperty("kits").EnumerateArray().ToArray();
        components[0].EnumerateObject().Select(field => field.Name).Should().Equal("ref");
        components[1].EnumerateObject().Select(field => field.Name).Should().Equal("ref", "args");
        components[1].GetProperty("args").EnumerateObject().Should().BeEmpty();
        components[2].EnumerateObject().Select(field => field.Name).Should().Equal("ref");
    }

    [Fact]
    public async Task PublishedConfigurationIsAV3MixinWithOnlyThePublicMetadataPayload()
    {
        // Arrange
        SaveBuild("ac");

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        var descriptor = new YamlDotNetParser().Read(Descriptors[^2], "config");
        descriptor.GetString("schemaVersion").Should().Be("3");
        descriptor.GetString("kind").Should().Be("mixin");
        Configurations.Should().ContainSingle();
    }

    [Fact]
    public async Task PublishedSquadMetadataExcludesLocalProvenanceAndKeepsNullAndEmptyPublicFields()
    {
        // Arrange
        SaveRegistryKitBuild();
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.CreatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05+00:00");
        build.Mcp.Add(new ManifestMcp { Name = "host-server" });
        build.ConfigFiles.Add(Root.File("private.abox", "secret: synthetic-private-value\n"));
        SaveVerifiedBuild(build);

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        using var actual = JsonDocument.Parse(Configurations.Single());
        AssertPublicJson(
            actual.RootElement,
            """
            {
              "SchemaVersion": 1,
              "Team": "sample",
              "Squad": "ac",
              "Tag": "latest",
              "CreatedAt": "2026-01-02T03:04:05+00:00",
              "Mcp": [{"Name": "host-server", "Url": null}],
              "State": [],
              "Kits": [{"Name": "tool", "Arguments": {}}],
              "Secrets": []
            }
            """
        );
        Configurations
            .Single()
            .Should()
            .NotContain(Root.Path)
            .And.NotContain("synthetic-private-value");
    }

    [Fact]
    public async Task PublicationPushesTheConfigurationMixinOfTheBuildWithItsSecretDeclarations()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        PublicationBuilds.SaveVerified(
            Store,
            build,
            [
                new SecretDeclaration(
                    "elastic",
                    "The API key for Elastic",
                    Hosts: ["elastic.example.com"],
                    EnvironmentVariable: "ELASTIC_API_KEY"
                ),
            ]
        );

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        using var actual = JsonDocument.Parse(Configurations.Single());
        actual.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(1);
        AssertPublicJson(
            actual.RootElement.GetProperty("Secrets"),
            """
            [{
              "Service": "elastic",
              "Prompt": "The API key for Elastic",
              "Hosts": ["elastic.example.com"],
              "Env": "ELASTIC_API_KEY",
              "Inputs": [],
              "Separator": ":",
              "Encoding": "plain",
              "Placeholder": null,
              "FromCommand": false
            }]
            """
        );
    }

    [Fact]
    public async Task BuildsWithoutAConfigurationMixinMustBeRebuilt()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.ConfigurationMixinDirectory = null;
        build.SourceHashes = BuildSources.Capture(build);
        Store.Save(build);

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*no squad configuration mixin*abox build --team sample --tag latest*"
        );
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyBuildsWithoutFingerprintsMustBeRebuilt()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.SourceHashes.Clear();
        Store.Save(build);

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*no source fingerprints*");
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedSourcesAreRejectedBeforeAnyPush()
    {
        // Arrange
        SaveBuild("ac");
        File.AppendAllText(
            Path.Combine(
                Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.WorkloadKitDirectory,
                "workload.yaml"
            ),
            "description: changed\n"
        );

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigurationFilesAndAddedPayloadFilesAreFingerprinted()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.ConfigFiles.Add(Root.File("squad.abox", "workload: sample\n"));
        SaveVerifiedBuild(build);
        File.AppendAllText(build.ConfigFiles[0], "state: [~/.private]\n");

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");

        // Arrange
        SaveVerifiedBuild(build);
        File.WriteAllText(
            Path.Combine(build.WorkloadKitDirectory, "new-payload.txt"),
            "not part of the build"
        );

        // Act / Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicationUsesSnapshotsNotSourcesChangedAfterValidation()
    {
        // Arrange
        SaveBuild("ac");
        var path = Path.Combine(
            Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.WorkloadKitDirectory,
            "workload.yaml"
        );
        var original = File.ReadAllText(path);
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("version")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                File.WriteAllText(path, original + "description: late change\n");
                return new ProcessResult(0, "", "");
            });

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        Descriptors[0].Should().Be(original);
        Calls
            .Where(call => call.Arguments.Contains("--push"))
            .Should()
            .OnlyContain(call =>
                !call.Arguments.Last().StartsWith(Root.Path, StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task SquadReferencesStayStableWhenAnotherSquadIsAdded()
    {
        // Arrange
        SaveBuild("ac");
        await Service.PublishAsync(Request("ac"), CancellationToken.None);
        var first = Calls.Single(call => call.Arguments.Contains("create")).Arguments;
        Calls.Clear();
        SaveBuild("bd");

        // Act
        await Service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        var again = Calls.First(call => call.Arguments.Contains("create")).Arguments;
        first[first.ToList().IndexOf("--tag") + 1]
            .Should()
            .Be(again[again.ToList().IndexOf("--tag") + 1]);
        Calls
            .Where(call => call.Arguments.Contains("--push"))
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(value => value.Contains("/ac/components/workload:"));
    }

    [Fact]
    public async Task TeamSquadAndComponentSegmentsCannotCollide()
    {
        // Arrange
        SaveBuild("ac");
        SaveBuild("workload");
        var second = Store.Load(new SquadBuildIdentity("sample", "workload", "latest"))!;
        second.Reference = "agentic-box-rwest-sample-ac:latest";
        Store.Save(second);
        var request = new PublishRequest(
            [new("sample", Root.Path, ["ac"]), new("sample-ac", Root.Path, ["workload"])],
            "latest"
        );

        // Act
        await Service.PublishAsync(request, CancellationToken.None);

        // Assert
        Calls
            .Where(call => call.Arguments.Contains("create"))
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest"
            )
            .And.Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample-ac/workload:latest"
            );
    }

    [Fact]
    public async Task CaseCollidingSquadsAreRejectedBeforeLoadingBuilds()
    {
        // Arrange: use the fixture with case-colliding squad selections.
        // Act
        var action = () => Service.PublishAsync(Request("ac", "AC"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*names collide*");
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CaseCollidingTeamsAreRejectedBeforeLoadingBuilds()
    {
        // Arrange
        var request = new PublishRequest(
            [new("sample", Root.Path, ["ac"]), new("SAMPLE", Root.Path, ["bd"])],
            "latest"
        );
        // Act
        var action = () => Service.PublishAsync(request, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*names collide*");
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRemoteAnnotationsStopPublicationBeforePromotion()
    {
        // Arrange
        SaveBuild("ac");
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "{}", ""));

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 workload descriptor*"
        );
        await Runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("create")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ACorruptSetCandidateNeverMovesTheRequestedTag()
    {
        // Arrange
        SaveBuild("ac");
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call =>
                    call.Arguments.Contains("--raw")
                    && !call.Arguments.Last().Contains("/components/")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "{}", ""));

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 workload descriptor*"
        );
        Calls.Count(call => call.Arguments.Contains("--push")).Should().Be(3);
        await Runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("create")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PlatformManifestsAreInspectedEvenWhenTheIndexIsUnannotated()
    {
        // Arrange
        SaveBuild("ac");
        var platform = "sha256:" + new string('b', 64);
        var attestation = "sha256:" + new string('c', 64);
        var index = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                mediaType = "application/vnd.oci.image.index.v1+json",
                manifests = new object[]
                {
                    new
                    {
                        digest = platform,
                        platform = new { os = "linux", architecture = "amd64" },
                    },
                    new
                    {
                        digest = attestation,
                        platform = new { os = "unknown", architecture = "unknown" },
                        annotations = new Dictionary<string, string>
                        {
                            ["vnd.docker.reference.type"] = "attestation-manifest",
                        },
                    },
                },
            }
        );
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call =>
                    call.Arguments.Contains("--raw")
                    && call.Arguments.Last().Contains("/components/workload")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ProcessResult(
                0,
                call.Arg<ProcessRequest>().Arguments.Last().EndsWith(platform)
                    ? PublishTestProcess.Manifest("workload")
                    : index,
                ""
            ));

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        await Runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Last().EndsWith(platform)),
                Arg.Any<CancellationToken>()
            );
        await Runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Last().EndsWith(attestation)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SuppliedLocalBuildPhaseArgumentsAreRejectedBeforeAnyPush()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var local = Path.Combine(Root.Path, "build-arg-kit");
        Directory.CreateDirectory(local);
        File.WriteAllText(
            Path.Combine(local, "spec.yaml"),
            "schemaVersion: \"3\"\nkind: mixin\nargs:\n  version:\n    buildArg: VERSION\n"
        );
        build.Kits.Add(
            new ManifestKit
            {
                IsLocal = true,
                Name = "tool",
                Reference = local,
                Arguments = new() { ["version"] = "1" },
            }
        );
        SaveVerifiedBuild(build);

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*build-phase arguments is not supported*"
        );
        Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SuppliedRegistryBuildPhaseArgumentsAreRejectedBeforeAnyPush()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Kits.Add(
            new ManifestKit
            {
                Name = "tool",
                Reference = "ghcr.io/example/tool:1",
                Arguments = new() { ["version"] = "1" },
            }
        );
        SaveVerifiedBuild(build);
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProcessResult(
                    0,
                    PublishTestProcess.Manifest(
                        "mixin",
                        new { version = new { buildArg = "VERSION" } }
                    ),
                    ""
                )
            );

        // Act
        var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*build-phase arguments is not supported*"
        );
        Calls.Should().OnlyContain(call => !call.Arguments.Contains("--push"));
    }

    [Fact]
    public async Task CleanupFailureDoesNotHideThePublishingError()
    {
        // Arrange
        SaveBuild("ac");
        string? blocked = null;
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var path = call.Arg<ProcessRequest>().Arguments.Last();
                while (
                    !Path.GetFileName(path).StartsWith("abox-publish-", StringComparison.Ordinal)
                )
                {
                    path = Path.GetDirectoryName(path)!;
                }

                Directory.Delete(path, true);
                File.WriteAllText(path, "blocks directory cleanup");
                blocked = path;
                return new ProcessResult(1, "", "original publication failure");
            });
        try
        {
            // Act
            var action = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

            // Assert
            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*original publication failure*"
            );
            Console.AllText.Should().Contain("warning: could not remove publication directory");
        }
        finally
        {
            if (blocked is not null)
                File.Delete(blocked);
        }
    }

    private string ManifestPath(SquadBuildIdentity identity) => _fixture.ManifestPath(identity);

    private void SaveBuild(
        string squad,
        string repository = "mtiae-developer-container-images-local"
    ) => _fixture.SaveBuild(squad, repository);

    [Fact]
    public async Task PublishingUsesTheOriginalVerifiedWorkloadNotTheLocalThinRecipe()
    {
        // Arrange
        SaveBuild("ac");
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        const string recipe = "FROM example.com/base:1\nRUN install-workload-tools\n";
        File.WriteAllText(Path.Combine(build.WorkloadKitDirectory, "workload.dockerfile"), recipe);
        build.LocalWorkload = LocalWorkloadFactory.Artifact(Root);
        SaveVerifiedBuild(build);
        string? publishedRecipe = null;
        Runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--push")
                    && request.Arguments.Any(argument => argument.Contains("/components/workload:"))
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                publishedRecipe = File.ReadAllText(
                    Path.Combine(request.Arguments.Last(), "workload.dockerfile")
                );
                return PublishTestProcess.Run(request);
            });

        // Act
        await Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        publishedRecipe.Should().Be(recipe);
        Configurations.Should().NotBeEmpty();
        Configurations
            .Should()
            .OnlyContain(configuration => !configuration.Contains(build.LocalWorkload.Reference));
    }

    private void SaveVerifiedBuild(BuildManifest build) => _fixture.SaveVerifiedBuild(build);
}
