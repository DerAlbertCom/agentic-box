using System.Text.Json;
using System.Text.Json.Nodes;
using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishedSquadSourceMcpTests
{
    private readonly PublishedSquadSourceFixture _fixture = new();
    private IProcessRunner Runner => _fixture.Runner;
    private IHostPrograms Programs => _fixture.Programs;
    private List<ProcessRequest> Calls => _fixture.Calls;
    private TestConsole Console => _fixture.Console;
    private JsonObject Metadata => _fixture.Metadata;

    private RemoteSquadSource Remote(AboxConfig config, IKitSourceTrust kitSources) =>
        new(
            config,
            kitSources,
            new PublishedSquadSource(new DockerPublishedImageReader(Programs, Runner), Console)
        );

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAndCreatePreserveRegisteredNamesAndExpandedMcpUrls(bool local)
    {
        // Arrange
        using var root = new TempDirectory();
        var paths = AboxPaths.For(root.CreateDirectory("config"), Path.Combine(root.Path, "state"));
        root.File(
            "teams/mti-ca/base.abox",
            """
            workload: sample
            mcp:
              - azuredevops
            parameters:
              endpoint: https://example.com
            """
        );
        root.File(
            "teams/mti-ca/ac.abox",
            """
            base: base
            mcp:
              - name: elastic
                url: ${{ parameters.endpoint }}/mcp
              - name: database
            """
        );
        root.File(
            "teams/mti-ca/sample.workload",
            "template: example.com/base:1\npublishRepository: test-images-local\n"
        );
        var config = new AboxConfig();
        var resolver = new SquadResolver(config, new YamlDotNetParser());
        var team = resolver.ResolveTeam(root.Path, "mti-ca");
        var store = new BuildStore(paths);
        var runner = LocalWorkloadFactory.Runner();
        var lifecycle = Substitute.For<ISandboxLifecycle>();
        var mcp = Substitute.For<IMcpServerRegistry>();
        mcp.ListAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["azuredevops", "database"]);
        mcp.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new McpServerRegistration(true, ""));
        lifecycle
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, ""));
        lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(""));
        var build = TestServiceGraphs.Build(resolver, store, runner, Console);
        await build.BuildAsync(
            new BuildRequest([team], root.Path, "latest"),
            CancellationToken.None
        );
        var manifest = LocalWorkloadFactory.Bundled(
            store.Load(new SquadBuildIdentity("mti-ca", "ac", "latest"))!
        );
        Metadata["Mcp"] = JsonSerializer.SerializeToNode(manifest.Mcp);
        Metadata["State"] = new JsonArray();
        var name = SandboxName.For(root.Path);
        lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.UtcNow) });
        var records = new SandboxRecordStore(paths);
        var creator = TestServiceGraphs.Creator(
            records,
            paths,
            lifecycle,
            mcp,
            Console,
            LocalWorkloadFactory.Source(store, runner),
            Remote(config, Substitute.For<IKitSourceTrust>()),
            new UserSecretsCopy(
                config,
                Substitute.For<IHostUserSecretStores>(),
                Substitute.For<ISandboxUserSecrets>()
            )
        );

        // Act
        var exitCode = await creator.CreateAsync(
            new SandboxCreationRequest(
                team,
                "ac",
                root.Path,
                "latest",
                FolderConfig: null,
                AssumeYes: true,
                Local: local
            ),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        manifest
            .Mcp.Should()
            .BeEquivalentTo(
                new ManifestMcp[]
                {
                    new() { Name = "azuredevops" },
                    new() { Name = "elastic", Url = "https://example.com/mcp" },
                    new() { Name = "database" },
                },
                options => options.WithStrictOrdering()
            );
        // Name-only declarations use existing registrations.
        await mcp.Received(1)
            .AddAsync("elastic", "https://example.com/mcp", Arg.Any<CancellationToken>());
        await mcp.Received(1)
            .AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await lifecycle.Received(1).RemoveAsync(name, Arg.Any<CancellationToken>());
        await lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.StaticMcpServers.SequenceEqual(
                        new[] { "azuredevops", "elastic", "database" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        records.Load(name)!.Team.Should().Be("mti-ca");
        Calls
            .Should()
            .OnlyContain(call =>
                !call.Arguments.Contains("build") && !call.Arguments.Contains("create")
            );
        if (!local)
            Calls
                .Should()
                .Contain(
                    call => call.Arguments[0] == "image",
                    "remote create must read the published metadata"
                );
    }
}
