using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Create;

/// <summary>
/// The create slice: <c>abox create</c>, using shared sandbox orchestration.
/// </summary>
internal static class CreateFeature
{
    public static IServiceCollection AddCreateFeature(this IServiceCollection services)
    {
        services.AddSingleton<Command, CreateCommand>();
        return services;
    }
}
