using Abox.Features.Build;
using Abox.Features.Create;
using Abox.Features.Ports;
using Abox.Features.Prune;
using Abox.Features.Publish;
using Abox.Features.Run;
using Abox.Features.Sandbox;
using Abox.Features.Secrets;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Launchers;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.SandboxCleanup;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.UserSecrets;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using Abox.Infrastructure.Herdr;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Cli;

/// <summary>
/// The composition root: the shared services and, one call each, the feature slices.
/// Every slice registers its own command, so the commands are built by the container,
/// never wired by hand.
/// </summary>
public static class AboxServiceCollection
{
    public static IServiceCollection AddAbox(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    )
    {
        services
            .AddSharedServices(console, trailing)
            .AddBuildFeature()
            .AddPublishFeature()
            .AddCreateFeature()
            .AddRunFeature()
            .AddLaunchers()
            .AddSecretsFeature()
            .AddUserSecretsFeature()
            .AddPortsFeature()
            .AddPruneFeature()
            .AddSandboxFeature();

        // The root holds all the slice commands, in registration order.
        services.AddSingleton<AboxRootCommand>();

        return services;
    }

    // The launchers that open apps through an external tool, after the run slice's own.
    private static IServiceCollection AddLaunchers(this IServiceCollection services) =>
        services.AddSingleton<ILauncher, HerdrAppLauncher>();

    private static IServiceCollection AddSharedServices(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    )
    {
        services.AddSingleton(console);
        services.AddSingleton(trailing);
        services.AddSingleton(AboxPaths.Create());
        // Commands describe their syntax eagerly, but resolve configuration-dependent work only in actions.
        services.AddSingleton(typeof(Lazy<>), typeof(LazyService<>));
        services.AddSingleton(provider =>
            AboxConfigLoader.Load(provider.GetRequiredService<AboxPaths>())
        );
        services.AddSingleton<ProcessOutput>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IOciRegistry, OciRegistry>();
        services.AddSingleton<IHostPrograms, HostPrograms>();
        services.AddSingleton<ISbxClient, SbxClient>();
        services.AddSingleton<ISandboxCleanup, SbxSandboxCleanup>();
        services.AddSingleton(UserSecretsHost.Create());
        services.AddSingleton<UserSecretsCopy>();
        services.AddSingleton<AppPorts>();
        services.AddSingleton<HostDevelopmentCertificate>();
        services.AddSingleton<SandboxHttps>();
        services.AddSingleton<Preflight>();
        services.AddSingleton<SquadResolver>();
        services.AddSingleton<SecretValues>();
        services.AddSingleton(SbxCredentialBindings.Create());
        services.AddSingleton<SquadCredentials>();
        services.AddSingleton<BuildStore>();
        services.AddSingleton<SandboxRecordStore>();
        services.AddSingleton<CreateRecoveryHints>();
        services.AddSingleton<FolderSandbox>();
        services.AddSingleton<PersonalConfigurations>();
        services.AddSingleton<LocalWorkloadImages>();
        services.AddSingleton<SandboxCreator>();
        services.AddSingleton<LocalSquadSource>();
        services.AddSingleton<RemoteSquadSource>();
        services.AddSingleton<IPublishedSquadSource, PublishedSquadSource>();
        return services;
    }
}

internal sealed class LazyService<T>(IServiceProvider provider)
    : Lazy<T>(provider.GetRequiredService<T>)
    where T : notnull;
