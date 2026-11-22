using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Sandbox;

/// <summary>
/// The sandbox slice: the commands that reach an existing sandbox directly. They are
/// not user commands; other programs call them and they stay out of the help.
/// </summary>
internal static class SandboxFeature
{
    public static IServiceCollection AddSandboxFeature(this IServiceCollection services)
    {
        services.AddScoped<Command, ExecCommand>();
        services.AddScoped<Command, PipeCommand>();
        services.AddScoped<ICommandHandler<SandboxExecParameters>, SandboxExecHandler>();
        return services;
    }
}
