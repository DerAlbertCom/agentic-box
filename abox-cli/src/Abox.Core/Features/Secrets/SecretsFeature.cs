using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Secrets;

/// <summary>
/// The secrets slice: <c>abox secrets</c> and the service that does its work.
/// </summary>
internal static class SecretsFeature
{
    public static IServiceCollection AddSecretsFeature(this IServiceCollection services)
    {
        services.AddScoped<SecretsService>();
        services.AddScoped<Command, SecretsCommand>();
        services.AddScoped<ICommandHandler<SecretsListParameters>, SecretsListHandler>();
        services.AddScoped<ICommandHandler<SecretsSetParameters>, SecretsSetHandler>();
        services.AddScoped<ICommandHandler<SecretsRemoveParameters>, SecretsRemoveHandler>();
        return services;
    }
}
