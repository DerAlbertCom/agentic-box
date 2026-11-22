using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Build;

/// <summary>
/// The build slice: <c>abox build</c> and the service that does its work.
/// </summary>
internal static class BuildFeature
{
    public static IServiceCollection AddBuildFeature(this IServiceCollection services)
    {
        services.AddScoped<BuildService>();
        services.AddScoped<KitBuildStep>();
        services.AddScoped<WorkloadKitGenerator>();
        services.AddScoped<StateMixinGenerator>();
        services.AddScoped<Command, BuildCommand>();
        services.AddScoped<ICommandHandler<BuildParameters>, BuildHandler>();
        return services;
    }
}
