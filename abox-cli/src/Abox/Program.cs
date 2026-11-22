using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

var console = InfrastructureServiceCollection.CreateSystemConsole();

return await CommandHandling.RunAsync(
    async () =>
    {
        var (arguments, trailing) = CommandLine.Split(args);
        var services = new ServiceCollection().AddAbox(
            console,
            new TrailingArguments { Values = trailing }
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        // One scope per process run: the root command and every handler come from it.
        await using var scope = provider.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<AboxRootCommand>()
            .InvokeAsync(arguments);
    },
    console
);
