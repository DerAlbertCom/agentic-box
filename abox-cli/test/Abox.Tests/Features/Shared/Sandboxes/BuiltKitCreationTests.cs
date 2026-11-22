using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuiltKitCreationTests
{
    private readonly SandboxCreatorFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;

    [Fact]
    public async Task LocalCreationUsesTheStagedBuiltKit()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("teams/sample/sample.workload", "template: example.com/sample:1\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(_root, "built", "echo generated > \"$1/generated.txt\"\n");
        var resolver = new SquadResolver(new AboxConfig());
        var team = resolver.ResolveTeam(_root.Path, "sample");
        await TestServiceGraphs
            .Build(
                resolver,
                new PersonalConfigurations(_fixture.Paths),
                _fixture.Store,
                LocalWorkloadFactory.Images(),
                _fixture.Console
            )
            .BuildAsync(
                new BuildRequest([team], _root.Path, "latest", Verify: false),
                CancellationToken.None
            );
        var mixins = new List<string>();
        _fixture
            .Lifecycle.CreateAsync(
                Arg.Do<SandboxDefinition>(request => mixins.AddRange(request.Mixins)),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SandboxCreation(string.Empty));

        // Act
        var exitCode = await _fixture.Service.CreateAsync(
            _fixture.Request() with
            {
                Team = team,
                Location = _root.CreateDirectory("location"),
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        mixins.Should().NotContain(Path.Combine(_root.Path, "kits", "built"));
        mixins
            .Should()
            .ContainSingle(mixin => Path.GetFileName(mixin) == "built")
            .Which.Should()
            .Match(mixin => File.Exists(Path.Combine(mixin, "generated.txt")));
    }

    [Fact]
    public async Task LocalCreationPassesTheSquadConfigurationMixinOfTheBuild()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            "workload: sample\nsecrets:\n  - service: elastic\n    prompt: The API key\n"
        );
        _root.File("teams/sample/sample.workload", "template: example.com/sample:1\n");
        var resolver = new SquadResolver(new AboxConfig());
        var team = resolver.ResolveTeam(_root.Path, "sample");
        await TestServiceGraphs
            .Build(
                resolver,
                new PersonalConfigurations(_fixture.Paths),
                _fixture.Store,
                LocalWorkloadFactory.Images(),
                _fixture.Console
            )
            .BuildAsync(
                new BuildRequest([team], _root.Path, "latest", Verify: false),
                CancellationToken.None
            );
        var mixins = new List<string>();
        _fixture
            .Lifecycle.CreateAsync(
                Arg.Do<SandboxDefinition>(request => mixins.AddRange(request.Mixins)),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SandboxCreation(string.Empty));

        // Act
        var exitCode = await _fixture.Service.CreateAsync(
            _fixture.Request() with
            {
                Team = team,
                Location = _root.CreateDirectory("location"),
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        var config = mixins
            .Should()
            .ContainSingle(mixin => Path.GetFileName(mixin) == "config")
            .Which;
        File.ReadAllText(Path.Combine(config, "config.dockerfile"))
            .Should()
            .Contain("COPY squad.json /usr/share/abox/squad.json");
        File.ReadAllText(Path.Combine(config, "squad.json"))
            .Should()
            .Contain("\"Service\": \"elastic\"")
            .And.Contain("\"Prompt\": \"The API key\"");
    }
}
