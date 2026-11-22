using Abox.Features.Build;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.KitInspection;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Shell;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Shared wiring only; callers supply their runtime, configuration and source policies.</summary>
internal static class TestServiceGraphs
{
    internal static BuildService Build(
        SquadResolver resolver,
        PersonalConfigurations personal,
        BuildStore store,
        LocalWorkloadImages images,
        IConsole console,
        IKitInspector? inspector = null,
        IKitBuildScript? buildScript = null
    ) =>
        new(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            personal,
            store,
            images,
            new KitBuildStep(buildScript ?? new ShellKitBuildScript(new ProcessRunner()), console),
            inspector ?? AcceptingKitInspector(),
            console
        );

    /// <summary>A kit inspector that accepts every kit.</summary>
    internal static IKitInspector AcceptingKitInspector()
    {
        var inspector = Substitute.For<IKitInspector>();
        inspector
            .InspectAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new KitInspectionOutcome(true, string.Empty));
        return inspector;
    }

    internal static SandboxCreator Creator(
        SandboxRecordStore records,
        AboxPaths paths,
        ISandboxLifecycle lifecycle,
        IMcpServerRegistry mcp,
        IConsole console,
        LocalSquadSource local,
        RemoteSquadSource remote,
        UserSecretsCopy copy,
        IHostSecretStore? secrets = null,
        ICredentialApprovals? approvals = null
    ) =>
        new(
            records,
            paths,
            SandboxFactory.Resolver(
                lifecycle,
                SandboxFactory.Hints(new AboxConfig(), new BuildStore(paths), records)
            ),
            lifecycle,
            mcp,
            console,
            local,
            remote,
            copy,
            SandboxFactory.Ports(Substitute.For<IApplicationPortPublisher>(), console),
            Credentials(
                paths,
                secrets ?? HostSecretStore(),
                approvals ?? Substitute.For<ICredentialApprovals>(),
                console
            )
        );

    /// <summary>The credential check of a squad from the personal configurations of the test.</summary>
    internal static SquadCredentials Credentials(
        AboxPaths paths,
        IHostSecretStore secrets,
        ICredentialApprovals approvals,
        IConsole console
    ) =>
        new(
            new SquadResolver(new AboxConfig(), new YamlDotNetParser()),
            new PersonalConfigurations(paths),
            secrets,
            approvals,
            new SecretValues(secrets, console),
            console,
            new YamlDotNetParser()
        );

    /// <summary>A host secret store that holds nothing and stores and removes every secret.</summary>
    internal static IHostSecretStore HostSecretStore()
    {
        var store = Substitute.For<IHostSecretStore>();
        store.ListAsync(Arg.Any<CancellationToken>()).Returns(new HostSecrets([], []));
        store
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(HostSecretChange.Done);
        store
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(HostSecretChange.Done);
        store
            .RemoveAsync(Arg.Any<SecretIdentity>(), Arg.Any<CancellationToken>())
            .Returns(HostSecretChange.Done);
        return store;
    }
}
