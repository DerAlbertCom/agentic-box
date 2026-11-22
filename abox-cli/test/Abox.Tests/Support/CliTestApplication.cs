using Abox.Cli;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Production CLI registration with caller-owned overrides and lazy provider lifetime.</summary>
internal sealed class CliTestApplication : IDisposable
{
    private ServiceProvider? provider;

    internal CliTestApplication(IConsole console, AboxPaths paths, TrailingArguments trailing)
    {
        Services = new ServiceCollection().AddAbox(console, trailing);
        Services.AddSingleton(paths);
        Services.AddSingleton(new SbxCredentialBindings(TestServiceGraphs.BindingsFile(paths)));
        Services.AddSingleton(Publisher);
    }

    /// <summary>Substituted so that no CLI test publishes ports through a real runner by accident.</summary>
    internal IApplicationPortPublisher Publisher { get; } =
        Substitute.For<IApplicationPortPublisher>();

    internal IServiceCollection Services { get; }
    internal ServiceProvider Provider => provider ??= Services.BuildServiceProvider();

    public void Dispose() => provider?.Dispose();
}
