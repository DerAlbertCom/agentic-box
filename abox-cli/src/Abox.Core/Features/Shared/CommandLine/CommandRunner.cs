using System.CommandLine;
using Abox.Features.Shared.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// Runs the action of every command: binds its parameters with the invocation defaults,
/// then resolves and runs the handler for those parameters. Turns abox errors into exit
/// code 1 and cancellation into exit code 130.
/// </summary>
public sealed class CommandRunner(IServiceProvider services, IConsole console)
{
    internal Task<int> RunAsync<TParameters>(
        AboxCommand<TParameters> command,
        ParseResult parseResult,
        CancellationToken cancellationToken
    )
        where TParameters : notnull =>
        CommandHandling.RunAsync(
            async () =>
            {
                var defaults = services.GetRequiredService<InvocationDefaults>();
                var parameters = await command.BindAsync(parseResult, defaults, cancellationToken);
                var handler = services.GetRequiredService<ICommandHandler<TParameters>>();
                return await handler.HandleAsync(parameters, cancellationToken);
            },
            console
        );
}
