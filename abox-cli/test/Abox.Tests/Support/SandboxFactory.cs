using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;

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

    public static AppPorts Ports(ISbxClient sbx, IConsole console) =>
        new(sbx, console, Https(sbx, console));

    public static SandboxHttps Https(ISbxClient sbx, IConsole console) =>
        new(new HostDevelopmentCertificate(LocalWorkloadFactory.Runner(), console), sbx, console);
}
