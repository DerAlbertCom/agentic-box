using Abox.Features.Run;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Launchers;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

internal sealed class RunFlowFixture
{
    internal TempDirectory Root { get; } = new();
    internal TestConsole Console { get; } = new();
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
            Lifecycle,
            Mcp,
            Console,
            LocalWorkloadFactory.Source(Store, console: Console),
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
            Store,
            Records,
            Create,
            Inspection,
            Exec,
            SandboxFactory.Https(Exec, Console),
            SandboxFactory.Resolver(Lifecycle, SandboxFactory.Hints(Config, Store, Records)),
            SandboxFactory.Hints(Config, Store, Records),
            hostPrograms,
            TestServiceGraphs.TokenExpiries(Paths, Console),
            Console
        );
        Inspection
            .HasProgramAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    /// <summary>The sandbox location of a folder, reading its folder configuration when asked for.</summary>
    internal static SandboxLocation Location(string path) =>
        new(
            path,
            cancellationToken =>
                FolderConfig.LoadAsync(path, new YamlDotNetParser(), cancellationToken)
        );
}
