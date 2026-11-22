using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Dotnet;
using Abox.Infrastructure.Sbx;
using Abox.Infrastructure.Shell;
using NSubstitute;

namespace Abox.Tests;

internal static class SandboxFactory
{
    public static FolderSandbox Resolver(ISandboxLifecycle lifecycle, CreateRecoveryHints hints) =>
        new(lifecycle, new ShellFolderRealPath(new ProcessRunner()), hints);

    public static CreateRecoveryHints Hints(
        AboxConfig config,
        BuildStore store,
        SandboxRecordStore records
    ) => new(config, new SquadResolver(config), store, records);

    public static AppPorts Ports(IApplicationPortPublisher publisher, IConsole console) =>
        new(publisher, console, Https(Exec(), console));

    public static SandboxHttps Https(ISandboxExec exec, IConsole console) =>
        new(
            new DotnetDevelopmentCertificate(LocalWorkloadFactory.Runner(), console),
            Certificates(),
            exec,
            console
        );

    public static readonly SandboxCertificateActivation Activated = new(
        SandboxCertificateActivationStatus.Activated
    );

    /// <summary>Sandbox HTTPS whose every step succeeds, with an empty export password.</summary>
    public static ISandboxDevelopmentCertificates Certificates()
    {
        var certificates = Substitute.For<ISandboxDevelopmentCertificates>();
        certificates
            .PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCertificatePreparation(SandboxCertificateStatus.Succeeded));
        certificates
            .ActivateAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Activated);
        return certificates;
    }

    /// <summary>A sandbox exec whose commands all succeed.</summary>
    public static ISandboxExec Exec()
    {
        var exec = Substitute.For<ISandboxExec>();
        exec.RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxExecOutcome(0));
        return exec;
    }
}
