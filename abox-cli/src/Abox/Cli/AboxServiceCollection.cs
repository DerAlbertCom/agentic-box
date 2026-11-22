using Abox.Features;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Cli;

/// <summary>
/// The composition root: each layer registers its own services, and the root command
/// holds the commands they add.
/// </summary>
public static class AboxServiceCollection
{
    public static IServiceCollection AddAbox(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    )
    {
        services.AddAboxCore(console, trailing).AddAboxInfrastructure();

        // The root holds all the slice commands, in registration order.
        services.AddScoped<AboxRootCommand>();

        return services;
    }
}
