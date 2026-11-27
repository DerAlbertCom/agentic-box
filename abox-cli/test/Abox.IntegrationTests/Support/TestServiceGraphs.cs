using Abox.Features.Build;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.GitHubTokens;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using Abox.Infrastructure.Shell;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Shared wiring only; callers supply their runtime, configuration and source policies.</summary>
internal static class TestServiceGraphs
{
    /// <summary>The build service on the Docker adapters, which run <c>docker</c> through <paramref name="runner"/>.</summary>
    internal static BuildService Build(
        SquadResolver resolver,
        BuildStore store,
        IProcessRunner runner,
        IConsole console,
        IKitBuildScript? buildScript = null,
        IHostPrograms? programs = null
    )
    {
        programs ??= LocalWorkloadFactory.Programs();
        var docker = new DockerComponentImages(programs, runner);
        return new(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            store,
            new ComponentImages(docker, docker, console),
            new DockerRegistryDigests(runner),
            new KitBuildStep(buildScript ?? new ShellKitBuildScript(new ProcessRunner()), console),
            new YamlDotNetParser(),
            console
        );
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
            ),
            new YamlDotNetParser()
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
            secrets,
            approvals,
            SecretValues(paths, secrets, console),
            TokenExpiries(paths, console),
            console,
            new YamlDotNetParser()
        );

    /// <summary>The expiries of the GitHub tokens, in the state directory of the test.</summary>
    internal static GitHubTokenExpiries TokenExpiries(AboxPaths paths, IConsole console) =>
        new(paths, TimeProvider.System, console);

    /// <summary>The secret values with a GitHub token sign-in that never reaches a host.</summary>
    internal static SecretValues SecretValues(
        AboxPaths paths,
        IHostSecretStore secrets,
        IConsole console
    ) =>
        new(
            secrets,
            console,
            new GitHubTokenSignIn(
                Substitute.For<IGitHubTokenCheck>(),
                TokenExpiries(paths, console),
                console
            )
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
