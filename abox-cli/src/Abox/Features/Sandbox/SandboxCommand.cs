using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;

namespace Abox.Features.Sandbox;

/// <summary>Common construction for the hidden commands that exec into a named sandbox.</summary>
internal static class SandboxCommand
{
    internal static void Configure(
        Command command,
        ISandboxExec exec,
        TrailingArguments trailing,
        IConsole console,
        SandboxExecInput input = SandboxExecInput.None
    )
    {
        command.Hidden = true;
        var sandbox = new Argument<string>("sandbox")
        {
            Description = "The sandbox to run the command in.",
        };
        command.Arguments.Add(sandbox);
        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var argv = trailing.Values;
                if (argv.Count == 0)
                {
                    console.WriteError(
                        $"error: no command given. Use: abox {command.Name} <sandbox> -- <command>"
                    );
                    return 1;
                }

                return await CommandHandling.RunAsync(
                    async () =>
                        (
                            await exec.RunAsync(
                                new SandboxExecRequest(parseResult.GetValue(sandbox)!, argv, input),
                                cancellationToken
                            )
                        ).ExitCode,
                    console
                );
            }
        );
    }
}
