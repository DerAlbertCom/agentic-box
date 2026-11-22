using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.UserSecrets;

internal static class UserSecretsFeature
{
    public static IServiceCollection AddUserSecretsFeature(this IServiceCollection services) =>
        services
            .AddScoped<Command, UserSecretsCommand>()
            .AddScoped<ICommandHandler<UserSecretsParameters>, UserSecretsHandler>();
}
