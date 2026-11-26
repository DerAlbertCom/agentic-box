using Abox.Cli;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Production CLI registration with caller-owned overrides and lazy provider lifetime.</summary>
internal sealed class CliTestApplication : IDisposable
{
    private ServiceProvider? _provider;
    private IServiceScope? _scope;

    internal CliTestApplication(IConsole console, AboxPaths paths, TrailingArguments trailing)
    {
        Services = new ServiceCollection().AddAbox(console, trailing);
        Services.AddSingleton(paths);
        Services.AddSingleton(Publisher);
        Services.AddSingleton(Certificates);
        Services.AddSingleton(HostSecrets);
        Services.AddSingleton(SquadFiles);
        Services.AddSingleton(Approvals);
    }

    /// <summary>Substituted so that no CLI test publishes ports through a real runner by accident.</summary>
    internal IApplicationPortPublisher Publisher { get; } =
        Substitute.For<IApplicationPortPublisher>();
    internal IHostSecretStore HostSecrets { get; } = TestServiceGraphs.HostSecretStore();
    internal ISandboxSquadFileReader SquadFiles { get; } =
        Substitute.For<ISandboxSquadFileReader>();
    internal ICredentialApprovals Approvals { get; } = Substitute.For<ICredentialApprovals>();

    /// <summary>Substituted so that no CLI test drives sandbox HTTPS through a real runner by accident.</summary>
    internal ISandboxDevelopmentCertificates Certificates { get; } = SandboxFactory.Certificates();

    internal IServiceCollection Services { get; }

    /// <summary>The invocation scope, opened once like the entry point opens it per process run.</summary>
    internal IServiceProvider Provider => (_scope ??= OpenScope()).ServiceProvider;
    internal ISandboxLifecycle Lifecycle { get; } = Substitute.For<ISandboxLifecycle>();
    internal IMcpServerRegistry Mcp { get; } = Substitute.For<IMcpServerRegistry>();
    internal IKitSourceTrust KitSources { get; } = Substitute.For<IKitSourceTrust>();
    internal ISandboxInspection Inspection { get; } = Substitute.For<ISandboxInspection>();

    /// <summary>Registers the sandbox runtime substitutes, for tests that substitute sbx itself.</summary>
    internal CliTestApplication WithSandboxSubstitutes()
    {
        Services.AddSingleton(Lifecycle);
        Services.AddSingleton(Mcp);
        Services.AddSingleton(KitSources);
        Services.AddSingleton(Inspection);
        return this;
    }

    private IServiceScope OpenScope()
    {
        _provider = Services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        return _provider.CreateScope();
    }

    public void Dispose()
    {
        _scope?.Dispose();
        _provider?.Dispose();
    }
}
