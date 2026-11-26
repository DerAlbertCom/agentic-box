using System.Text.Json;
using System.Text.Json.Nodes;
using Abox.Features.Build;
using Abox.Features.Publish;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceTeamTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private BuildStore Store => _fixture.Store;
    private TestConsole Console => _fixture.Console;
    private List<ProcessRequest> Calls => _fixture.Calls;
    private List<string> Descriptors => _fixture.Descriptors;
    private List<string> Configurations => _fixture.Configurations;
    private PublishService Service => _fixture.Service;

    private PublishRequest Request(params string[] squads) => _fixture.Request(squads);

    private void SaveBuild(string squad) => _fixture.SaveBuild(squad);

    private string ManifestPath(SquadBuildIdentity identity) => _fixture.ManifestPath(identity);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublicationSelectsEachTeamsSharedSquadWithoutChangingPublishedNames(
        bool reverse,
        bool metadataOnly
    )
    {
        // Arrange
        var resolver = new SquadResolver(new AboxConfig(), new YamlDotNetParser());
        foreach (var team in new[] { "sample", "other" })
        {
            Root.File($"teams/{team}/ac.abox", $"workload: {team}\nstate: [~/.{team}]\n");
            Root.File(
                $"teams/{team}/{team}.workload",
                $"template: example.com/{team}:1\npublishRepository: {team}-images-local\n"
            );
        }
        var names = reverse ? new[] { "other", "sample" } : new[] { "sample", "other" };
        var teams = names.Select(team => resolver.ResolveTeam(Root.Path, team)).ToList();
        var build = TestServiceGraphs.Build(
            resolver,
            Store,
            LocalWorkloadFactory.Runner(),
            Console
        );
        foreach (var team in teams)
        {
            await build.BuildAsync(
                new BuildRequest([team], Root.Path, "latest"),
                CancellationToken.None
            );
        }

        // Act
        var exitCode = await Service.PublishAsync(
            new PublishRequest(teams, "latest"),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        Configurations.Should().HaveCount(2);
        foreach (var team in names)
        {
            Descriptors
                .Should()
                .Contain(descriptor => descriptor.Contains($"displayName: \"{team}\"\n"));
            var configuration = Configurations.Single(text =>
                JsonNode.Parse(text)!["Team"]!.GetValue<string>() == team
            );
            using var json = JsonDocument.Parse(configuration);
            json.RootElement.GetProperty("Squad").GetString().Should().Be("ac");
            json.RootElement.GetProperty("State")[0]
                .GetProperty("SandboxPath")
                .GetString()
                .Should()
                .Be("/home/agent/." + team);
            Calls
                .Where(call => call.Arguments.Contains("create"))
                .Should()
                .Contain(call =>
                    call.Arguments.Contains(
                        $"rwest.jfrog.io/{team}-images-local/agentic-box-rwest-{team}/ac:latest"
                    )
                );
        }
    }

    [Theory]
    [InlineData("sample", true)]
    [InlineData("other", false)]
    [InlineData(null, false)]
    public async Task LegacyPublicationRequiresExplicitMatchingOwnershipWithoutMigration(
        string? owner,
        bool allowed
    )
    {
        // Arrange
        SaveBuild("ac");
        var manifest = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        manifest.Reference = owner is null ? null : $"agentic-box-rwest-{owner}:latest";
        var original = JsonSerializer.Serialize(manifest);
        var legacy = Root.File("state/builds/ac/latest/build.json", original);
        var qualified = ManifestPath(new("sample", "ac", "latest"));
        File.Delete(qualified);

        // Act
        var publish = () => Service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        if (allowed)
        {
            (await publish()).Should().Be(0);
            Configurations.Should().ContainSingle();
        }
        else
        {
            (await publish.Should().ThrowAsync<AboxException>()).WithMessage(
                "*abox build --team sample --tag latest*"
            );
            Calls.Should().BeEmpty();
        }
        File.ReadAllText(legacy).Should().Be(original);
        File.Exists(qualified).Should().BeFalse();
    }
}
