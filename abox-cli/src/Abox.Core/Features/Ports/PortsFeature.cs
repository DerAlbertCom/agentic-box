using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Ports;

/// <summary>
/// The ports slice: <c>abox ports</c>, using shared application ports and sandbox HTTPS.
/// </summary>
internal static class PortsFeature
{
    public static IServiceCollection AddPortsFeature(this IServiceCollection services) =>
        services
            .AddScoped<Command, PortsCommand>()
            .AddScoped<ICommandHandler<PortsParameters>, PortsHandler>();
}
