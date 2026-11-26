using System.Text.Json;
using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorTeamTests
{
    private readonly SandboxCreatorFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private TestConsole Console => _fixture.Console;
    private ISandboxLifecycle Lifecycle => _fixture.Lifecycle;
    private IMcpServerRegistry Mcp => _fixture.Mcp;
    private IPublishedSquadSource Published => _fixture.Published;
    private AboxPaths Paths => _fixture.Paths;
    private BuildStore Store => _fixture.Store;
    private SandboxRecordStore Records => _fixture.Records;
    private SandboxCreator Service => _fixture.Service;

    private SandboxCreationRequest Request() => _fixture.Request();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalCreationUsesEachTeamsSharedSquadBuild(bool reverse)
    {
        // Arrange
        var teams = await BuildSharedSquadsAsync(reverse);

        // Act
        foreach (var team in teams)
        {
            var location = Root.CreateDirectory("locations/" + team.Name);
            (
                await Service.CreateAsync(
                    Request() with
                    {
                        Team = team,
                        Location = location,
                    },
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
        }

        // Assert
        foreach (var team in teams)
        {
            var build = Store.Load(new SquadBuildIdentity(team.Name, "ac", "latest"))!;
            var kits = Path.Combine(
                Store.DirectoryFor(new SquadBuildIdentity(team.Name, "ac", "latest")),
                LocalSquadSource.ThinKitsFolderName
            );
            var location = Path.Combine(Root.Path, "locations", team.Name);
            await Lifecycle
                .Received(1)
                .CreateAsync(
                    Arg.Is<SandboxDefinition>(request =>
                        request.Name == SandboxName.For(location)
                        && request.KitSource == Path.Combine(kits, "workload")
                        && request.Mixins.Contains(Path.Combine(kits, "state"))
                        && request.Mounts.Single().SandboxPath == "/home/agent/." + team.Name
                        && request.PullPolicy == PullPolicy.Never
                    ),
                    Arg.Any<CancellationToken>()
                );
            var record = Records.Load(SandboxName.For(location))!;
            record.Team.Should().Be(team.Name);
            record.BuildCreatedAt.Should().Be(build.CreatedAt);
            BuildSources.EnsureUnchanged(build);
        }
        Published.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("agentic-box-rwest-other:latest")]
    public async Task UnownedLegacyLocalCreationCannotPrepareOrReplaceASandbox(string? reference)
    {
        // Arrange
        Root.File(
            "state/builds/ac/latest/build.json",
            JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = reference,
                }
            )
        );

        // Act
        var create = () =>
            Service.CreateAsync(Request() with { AssumeYes = true }, CancellationToken.None);

        // Assert
        (await create.Should().ThrowAsync<AboxException>()).WithMessage(
            "*abox build --team sample --tag latest*"
        );
        Lifecycle.ReceivedCalls().Should().BeEmpty();
        Mcp.ReceivedCalls().Should().BeEmpty();
        Records.Load(SandboxName.For(Root.Path)).Should().BeNull();
    }

    private async Task<IReadOnlyList<SquadResolver.ResolvedTeam>> BuildSharedSquadsAsync(
        bool reverse
    )
    {
        foreach (var team in new[] { "sample", "other" })
        {
            Root.File($"teams/{team}/ac.abox", $"workload: {team}\nstate: [~/.{team}]\n");
            Root.File(
                $"teams/{team}/{team}.workload",
                $"template: example.com/{team}:1\npublishRepository: test-images-local\n"
            );
        }
        var resolver = new SquadResolver(new AboxConfig(), new YamlDotNetParser());
        var names = reverse ? new[] { "other", "sample" } : new[] { "sample", "other" };
        var teams = names.Select(team => resolver.ResolveTeam(Root.Path, team)).ToList();
        var build = TestServiceGraphs.Build(
            resolver,
            Store,
            LocalWorkloadFactory.Runner(),
            Console
        );
        await build.BuildAsync(
            new BuildRequest(teams, Root.Path, "latest"),
            CancellationToken.None
        );
        return teams;
    }
}
