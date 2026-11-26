using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.Launchers;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Registry;
using Abox.Features.Shared.SandboxCleanup;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure.Docker;
using Abox.Infrastructure.Dotnet;
using Abox.Infrastructure.Herdr;
using Abox.Infrastructure.Sbx;
using Abox.Infrastructure.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Infrastructure;

/// <summary>
/// The adapters: each external tool's implementation of the use-case interfaces, the
/// process runner, the YAML parser and the launchers that drive an external tool.
/// </summary>
public static class InfrastructureServiceCollection
{
    public static IServiceCollection AddAboxInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IYamlParser, YamlDotNetParser>();
        services.AddScoped<ProcessOutput>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        services.AddScoped<DockerComponentImages>();
        services.AddScoped<IComponentImageBuilder>(provider =>
            provider.GetRequiredService<DockerComponentImages>()
        );
        services.AddScoped<ILocalImageStore>(provider =>
            provider.GetRequiredService<DockerComponentImages>()
        );
        services.AddScoped<ILocalImageArchives>(provider =>
            provider.GetRequiredService<DockerComponentImages>()
        );
        services.AddScoped<IRegistryPublisher, DockerRegistryPublisher>();
        services.AddScoped<IRegistryDigests, DockerRegistryDigests>();
        services.AddScoped<IPublishedImageReader, DockerPublishedImageReader>();
        services.AddScoped<IHostPrograms, ShellHostPrograms>();
        services.AddScoped<SbxInvoker>();
        services.AddScoped<IApplicationPortPublisher, SbxApplicationPorts>();
        services.AddScoped<ISandboxCleanup, SbxSandboxCleanup>();
        services.AddScoped<ISandboxExec, SbxSandboxExec>();
        services.AddScoped<IHostUserSecretStores>(_ => DotnetUserSecretStores.Create());
        services.AddScoped<ISandboxUserSecrets, SbxUserSecrets>();
        services.AddScoped<IHostSecretStore, SbxHostSecretStore>();
        services.AddScoped<ISandboxSquadFileReader, SbxSandboxSquadFileReader>();
        services.AddScoped<ISandboxLifecycle, SbxSandboxLifecycle>();
        services.AddScoped<IMcpServerRegistry, SbxMcpServerRegistry>();
        services.AddScoped<IKitSourceTrust, SbxKitSourceTrust>();
        services.AddScoped<ISandboxInspection, SbxSandboxInspection>();
        services.AddScoped<IFolderRealPath, ShellFolderRealPath>();
        services.AddScoped<IHostDevelopmentCertificate, DotnetDevelopmentCertificate>();
        services.AddScoped<ISandboxDevelopmentCertificates, SbxDevelopmentCertificates>();
        services.AddScoped<IPreflightChecks, SbxPreflightChecks>();
        services.AddScoped<ICredentialApprovals>(_ => SbxCredentialBindings.Create());
        services.AddScoped<IKitBuildScript, ShellKitBuildScript>();
        // The launchers that open apps through an external tool, after the run slice's own.
        services.AddScoped<ILauncher, HerdrAppLauncher>();
        return services;
    }

    /// <summary>The console of the real terminal, for the entry point.</summary>
    public static IConsole CreateSystemConsole() => new SystemConsole();
}
