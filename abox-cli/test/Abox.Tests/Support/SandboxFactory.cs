using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

internal static class SandboxFactory
{
    public static FolderSandbox Resolver(ISbxClient sbx, CreateRecoveryHints hints) =>
        new(sbx, new ProcessRunner(), hints);

    public static CreateRecoveryHints Hints(
        AboxConfig config,
        BuildStore store,
        SandboxRecordStore records
    ) => new(config, new SquadResolver(config), store, records);

    public static AppPorts Ports(
        IApplicationPortPublisher publisher,
        ISbxClient sbx,
        IConsole console
    ) => new(publisher, console, Https(sbx, Exec(), console));

    public static SandboxHttps Https(ISbxClient sbx, ISandboxExec exec, IConsole console) =>
        new(
            new HostDevelopmentCertificate(LocalWorkloadFactory.Runner(), console),
            sbx,
            exec,
            console
        );

    /// <summary>A sandbox exec whose commands all succeed.</summary>
    public static ISandboxExec Exec()
    {
        var exec = Substitute.For<ISandboxExec>();
        exec.RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxExecOutcome(0));
        return exec;
    }
}
