using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.KitInspection;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

internal sealed class BuildServiceFixture
{
    internal TempDirectory Root { get; } = new();
    internal IKitInspector Inspector { get; } = TestServiceGraphs.AcceptingKitInspector();
    internal IKitBuildScript BuildScript { get; } = Substitute.For<IKitBuildScript>();
    internal IProcessRunner Runner { get; } = LocalWorkloadFactory.Runner();
    internal TestConsole Console { get; } = new();
    internal BuildStore Store { get; }
    internal SquadResolver Resolver { get; } = new(new AboxConfig(), new YamlDotNetParser());
    internal BuildService Service { get; }

    internal BuildServiceFixture()
    {
        var paths = AboxPaths.For(
            Path.Combine(Root.Path, "config"),
            Path.Combine(Root.Path, "state")
        );
        Store = new BuildStore(paths);
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        Root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        Root.File("config/personal.abox", "kits: [private]\n");
        Root.File("kits/private/private.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        Service = TestServiceGraphs.Build(
            Resolver,
            new PersonalConfigurations(paths),
            Store,
            LocalWorkloadFactory.Images(Runner),
            Console,
            Inspector,
            BuildScript
        );
    }
}
