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
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Features.UserSecrets;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features;

/// <summary>
/// The core of abox: the shared services and, one call each, the feature slices. Every
/// slice registers its own command, so the commands are built by the container, never
/// wired by hand. The use-case interfaces are implemented elsewhere.
/// </summary>
public static class CoreServiceCollection
{
    public static IServiceCollection AddAboxCore(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    ) =>
        services
            .AddSharedServices(console, trailing)
            .AddBuildFeature()
            .AddPublishFeature()
            .AddCreateFeature()
            .AddRunFeature()
            .AddSecretsFeature()
            .AddUserSecretsFeature()
            .AddPortsFeature()
            .AddPruneFeature()
            .AddSandboxFeature();

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
            AboxConfigLoader.Load(
                provider.GetRequiredService<AboxPaths>(),
                provider.GetRequiredService<IYamlParser>()
            )
        );
        services.AddSingleton<UserSecretsCopy>();
        services.AddSingleton<AppPorts>();
        services.AddSingleton<SandboxHttps>();
        services.AddSingleton<Preflight>();
        services.AddSingleton<SquadResolver>();
        services.AddSingleton<SecretValues>();
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
