using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Launchers;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Run;

/// <summary>
/// The run slice: <c>abox run</c> and the service that does its work.
/// </summary>
internal static class RunFeature
{
    public static IServiceCollection AddRunFeature(this IServiceCollection services)
    {
        services.AddSingleton<RunFlow>();
        services.AddSingleton<ILauncher, ShellLauncher>();
        services.AddSingleton<LauncherRegistry>();
        services.AddSingleton<Command, RunCommand>();
        services.AddScoped<ICommandHandler<RunParameters>, RunHandler>();
        return services;
    }
}
