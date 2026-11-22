using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// Runs commands with <c>sbx exec</c>, each wrapped in the protected HTTPS bootstrap.
/// </summary>
public sealed class SbxSandboxExec(SbxInvoker invoker) : ISandboxExec
{
    public async Task<SandboxExecOutcome> RunAsync(
        SandboxExecRequest request,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string> { "exec" };

        if (request.Input is not SandboxExecInput.None)
        {
            arguments.Add("-i");
        }

        if (request.Input is SandboxExecInput.Terminal)
        {
            arguments.Add("-t");
        }

        if (request.WorkingDirectory is not null)
        {
            arguments.Add("-w");
            arguments.Add(request.WorkingDirectory);
        }

        foreach (var (name, value) in request.Environment ?? new Dictionary<string, string>())
        {
            arguments.Add("-e");
            arguments.Add($"{name}={value}");
        }

        arguments.Add(request.Sandbox);
        SbxDevelopmentCertificates.AppendExecCommand(arguments, request.Command);

        var result = await invoker.RunAsync(
            invoker.Call(arguments) with
            {
                AttachConsole = true,
            },
            cancellationToken
        );

        return new SandboxExecOutcome(result.ExitCode);
    }
}
