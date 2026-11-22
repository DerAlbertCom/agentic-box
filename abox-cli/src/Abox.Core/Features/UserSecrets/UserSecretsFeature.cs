using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.UserSecrets;

/// <summary>
/// The user-secrets slice: <c>abox user-secrets</c>, using the shared user-secrets copy.
/// </summary>
internal static class UserSecretsFeature
{
    public static IServiceCollection AddUserSecretsFeature(this IServiceCollection services) =>
        services
            .AddScoped<Command, UserSecretsCommand>()
            .AddScoped<ICommandHandler<UserSecretsParameters>, UserSecretsHandler>();
}
