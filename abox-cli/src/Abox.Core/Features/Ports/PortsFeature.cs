using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Ports;

internal static class PortsFeature
{
    public static IServiceCollection AddPortsFeature(this IServiceCollection services) =>
        services
            .AddSingleton<Command, PortsCommand>()
            .AddScoped<ICommandHandler<PortsParameters>, PortsHandler>();
}
