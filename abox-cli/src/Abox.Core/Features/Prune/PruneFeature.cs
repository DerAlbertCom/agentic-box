using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Prune;

/// <summary>
/// The prune slice: <c>abox prune</c> and the service that does its work.
/// </summary>
internal static class PruneFeature
{
    public static IServiceCollection AddPruneFeature(this IServiceCollection services)
    {
        services.AddScoped<PruneService>();
        services.AddScoped<Command, PruneCommand>();
        services.AddScoped<ICommandHandler<PruneParameters>, PruneHandler>();
        return services;
    }
}
