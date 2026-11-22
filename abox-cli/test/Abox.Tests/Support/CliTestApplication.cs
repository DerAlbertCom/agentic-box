using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;

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
    }

    internal IServiceCollection Services { get; }
    internal ServiceProvider Provider => provider ??= Services.BuildServiceProvider();

    public void Dispose() => provider?.Dispose();
}
