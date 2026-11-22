using System.CommandLine;
using Abox.Features.Shared.Launchers;
using Abox.Infrastructure.Herdr;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Run;

/// <summary>
/// The run slice: <c>abox run</c> and the service that does its work.
/// </summary>
public static class RunFeature
{
    public static IServiceCollection AddRunFeature(this IServiceCollection services)
    {
        services.AddSingleton<RunFlow>();
        services.AddSingleton<ILauncher, ShellLauncher>();
        services.AddSingleton<ILauncher, HerdrAppLauncher>();
        services.AddSingleton<LauncherRegistry>();
        services.AddSingleton<Command, RunCommand>();
        return services;
    }
}
