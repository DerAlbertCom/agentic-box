using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Publish;

/// <summary>
/// The publish slice: <c>abox publish</c> and the service that does its work.
/// </summary>
internal static class PublishFeature
{
    public static IServiceCollection AddPublishFeature(this IServiceCollection services)
    {
        services.AddScoped<PublishService>();
        services.AddScoped<Command, PublishCommand>();
        services.AddScoped<ICommandHandler<PublishParameters>, PublishHandler>();
        return services;
    }
}
