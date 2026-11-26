using Abox.Features.Build;
using Abox.Features.Publish;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuiltKitPublicationTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;

    [Fact]
    public async Task PublicationPushesTheStagedBuiltKit()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        Root.File(
            "teams/sample/sample.workload",
            "template: example.com/sample:1\npublishRepository: sample-images-local\n"
        );
        Root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(Root, "built", "echo generated > \"$1/generated.txt\"\n");
        var resolver = new SquadResolver(new AboxConfig(), new YamlDotNetParser());
        var team = resolver.ResolveTeam(Root.Path, "sample");
        await TestServiceGraphs
            .Build(resolver, _fixture.Store, LocalWorkloadFactory.Runner(), _fixture.Console)
            .BuildAsync(new BuildRequest([team], Root.Path, "latest"), CancellationToken.None);
        var pushedKits = new List<string>();
        _fixture
            .Runner.When(runner =>
                runner.RunAsync(
                    Arg.Is<ProcessRequest>(request =>
                        request.Arguments.Contains("push")
                        && request.Arguments.Last().Contains("/components/kits/built:")
                    ),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call =>
                pushedKits.Add(
                    ReadGenerated(
                        LocalWorkloadFactory.BuiltFrom(call.Arg<ProcessRequest>().Arguments[^1])!
                    )
                )
            );

        // Act
        var exitCode = await _fixture.Service.PublishAsync(
            new PublishRequest([team], "latest"),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        pushedKits.Should().Equal("generated\n");
    }

    private static string ReadGenerated(string context)
    {
        var generated = Path.Combine(context, "generated.txt");
        return File.Exists(generated) ? File.ReadAllText(generated) : "(missing)";
    }
}
