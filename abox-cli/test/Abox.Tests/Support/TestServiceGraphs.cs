using Abox.Features.Build;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.KitBuildSteps;
using Abox.Features.Shared.KitInspection;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
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
        ISbxClient sbx,
        IConsole console,
        LocalSquadSource local,
        RemoteSquadSource remote,
        UserSecretsCopy copy
    ) =>
        new(
            records,
            paths,
            SandboxFactory.Resolver(
                sbx,
                SandboxFactory.Hints(new AboxConfig(), new BuildStore(paths), records)
            ),
            sbx,
            console,
            local,
            remote,
            copy,
            SandboxFactory.Ports(Substitute.For<IApplicationPortPublisher>(), sbx, console),
            Credentials(paths, sbx, console)
        );

    /// <summary>The credential check, with the bindings of sbx in the state directory of the test.</summary>
    internal static SquadCredentials Credentials(
        AboxPaths paths,
        ISbxClient sbx,
        IConsole console
    ) =>
        new(
            new SquadResolver(new AboxConfig()),
            new PersonalConfigurations(paths),
            sbx,
            new SbxCredentialBindings(BindingsFile(paths)),
            new SecretValues(sbx, console),
            console
        );

    internal static string BindingsFile(AboxPaths paths) =>
        Path.Combine(paths.StateDirectory, "sbx", "credentials.yaml");
}
