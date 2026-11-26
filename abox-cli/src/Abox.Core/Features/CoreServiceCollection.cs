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
        // The one real terminal, shared by every invocation scope.
        services.AddSingleton(console);
        services.AddScoped(_ => trailing);
        services.AddScoped<Verbosity>();
        services.AddScoped(_ => AboxPaths.Create());
        services.AddScoped(provider =>
            AboxConfigLoader.Load(
                provider.GetRequiredService<AboxPaths>(),
                provider.GetRequiredService<IYamlParser>()
            )
        );
        services.AddScoped<CommandRunner>();
        // The user configuration is loaded only when an action first asks for a default.
        services.AddScoped(provider => new InvocationDefaults(
            new Lazy<AboxConfig>(provider.GetRequiredService<AboxConfig>),
            provider.GetRequiredService<IYamlParser>(),
            provider.GetRequiredService<TrailingArguments>()
        ));
        services.AddScoped<SourceRootResolver>();
        services.AddScoped<UserSecretsCopy>();
        services.AddScoped<AppPorts>();
        services.AddScoped<SandboxHttps>();
        services.AddScoped<Preflight>();
        services.AddScoped<SquadResolver>();
        services.AddScoped<SecretValues>();
        services.AddScoped<SquadCredentials>();
        services.AddScoped<BuildStore>();
        services.AddScoped<SandboxRecordStore>();
        services.AddScoped<CreateRecoveryHints>();
        services.AddScoped<FolderSandbox>();
        services.AddScoped<SandboxCreator>();
        services.AddScoped<LocalSquadSource>();
        services.AddScoped<RemoteSquadSource>();
        services.AddScoped<IPublishedSquadSource, PublishedSquadSource>();
        return services;
    }
}
