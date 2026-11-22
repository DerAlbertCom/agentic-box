using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Sandbox;

/// <summary>Runs the command of <c>abox exec</c> or <c>abox pipe</c> in the named sandbox.</summary>
internal sealed class SandboxExecHandler(ISandboxExec exec) : ICommandHandler<SandboxExecParameters>
{
    public async Task<int> HandleAsync(
        SandboxExecParameters parameters,
        CancellationToken cancellationToken
    ) =>
        (
            await exec.RunAsync(
                new SandboxExecRequest(parameters.Sandbox, parameters.Command, parameters.Input),
                cancellationToken
            )
        ).ExitCode;
}
