using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.KitInspection;
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
        services.AddSingleton<IYamlParser, YamlDotNetParser>();
        services.AddSingleton<ProcessOutput>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<DockerWorkloadImages>();
        services.AddSingleton<IWorkloadImageBuilder>(provider =>
            provider.GetRequiredService<DockerWorkloadImages>()
        );
        services.AddSingleton<ILocalWorkloadImageStore>(provider =>
            provider.GetRequiredService<DockerWorkloadImages>()
        );
        services.AddSingleton<IRegistryPublisher, DockerRegistryPublisher>();
        services.AddSingleton<IPublishedImageReader, DockerPublishedImageReader>();
        services.AddSingleton<IHostPrograms, ShellHostPrograms>();
        services.AddSingleton<SbxInvoker>();
        services.AddSingleton<IApplicationPortPublisher, SbxApplicationPorts>();
        services.AddSingleton<ISandboxCleanup, SbxSandboxCleanup>();
        services.AddSingleton<ISandboxExec, SbxSandboxExec>();
        services.AddSingleton<IHostUserSecretStores>(DotnetUserSecretStores.Create());
        services.AddSingleton<ISandboxUserSecrets, SbxUserSecrets>();
        services.AddSingleton<IHostSecretStore, SbxHostSecretStore>();
        services.AddSingleton<ISandboxSquadFileReader, SbxSandboxSquadFileReader>();
        services.AddSingleton<ISandboxLifecycle, SbxSandboxLifecycle>();
        services.AddSingleton<IMcpServerRegistry, SbxMcpServerRegistry>();
        services.AddSingleton<IKitSourceTrust, SbxKitSourceTrust>();
        services.AddSingleton<ISandboxInspection, SbxSandboxInspection>();
        services.AddSingleton<IFolderRealPath, ShellFolderRealPath>();
        services.AddSingleton<IHostDevelopmentCertificate, DotnetDevelopmentCertificate>();
        services.AddSingleton<ISandboxDevelopmentCertificates, SbxDevelopmentCertificates>();
        services.AddSingleton<IPreflightChecks, SbxPreflightChecks>();
        services.AddSingleton<ICredentialApprovals>(SbxCredentialBindings.Create());
        services.AddSingleton<IKitInspector, SbxKitInspector>();
        services.AddSingleton<IKitBuildScript, ShellKitBuildScript>();
        // The launchers that open apps through an external tool, after the run slice's own.
        services.AddSingleton<ILauncher, HerdrAppLauncher>();
        return services;
    }

    /// <summary>The console of the real terminal, for the entry point.</summary>
    public static IConsole CreateSystemConsole() => new SystemConsole();
}
