using Abox.Features.Run;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Launchers;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

internal sealed class RunFlowFixture
{
    internal TempDirectory Root { get; } = new();
    internal TestConsole Console { get; } = new();
    internal ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    internal ISandboxLifecycle Lifecycle { get; } = Substitute.For<ISandboxLifecycle>();
    internal IMcpServerRegistry Mcp { get; } = Substitute.For<IMcpServerRegistry>();
    internal ISandboxInspection Inspection { get; } = Substitute.For<ISandboxInspection>();
    internal ISandboxExec Exec { get; } = SandboxFactory.Exec();
    internal RunFlow Service { get; }
    internal AboxPaths Paths { get; }
    internal BuildStore Store { get; }
    internal SandboxRecordStore Records { get; }
    internal SandboxCreator Create { get; }
    internal AboxConfig Config { get; } = new() { DefaultTeam = "sample" };

    internal RunFlowFixture()
    {
        var appLauncher = Substitute.For<ILauncher>();
        appLauncher.Id.Returns("app");
        appLauncher.HostRequirements.Returns([]);
        appLauncher.SandboxRequirements.Returns(["app-core"]);
        var launchers = new LauncherRegistry([new ShellLauncher(), appLauncher]);
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);
        Paths = AboxPaths.For(Path.Combine(Root.Path, "config"), Path.Combine(Root.Path, "state"));
        Store = new BuildStore(Paths);
        Records = new SandboxRecordStore(Paths);
        Create = TestServiceGraphs.Creator(
            Records,
            Paths,
            Sbx,
            Lifecycle,
            Mcp,
            Console,
            new LocalSquadSource(Store, LocalWorkloadFactory.Images(), Console),
            new RemoteSquadSource(
                Config,
                Substitute.For<IKitSourceTrust>(),
                PublishedSquadFactory.Source()
            ),
            new UserSecretsCopy(
                Config,
                Substitute.For<IHostUserSecretStores>(),
                Substitute.For<ISandboxUserSecrets>()
            )
        );
        Service = new RunFlow(
            Config,
            launchers,
            new SquadResolver(Config),
            Store,
            Records,
            Create,
            Inspection,
            Exec,
            SandboxFactory.Https(Sbx, Exec, Console),
            SandboxFactory.Resolver(Lifecycle, SandboxFactory.Hints(Config, Store, Records)),
            SandboxFactory.Hints(Config, Store, Records),
            hostPrograms,
            Console
        );
        Inspection
            .HasProgramAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }
}
