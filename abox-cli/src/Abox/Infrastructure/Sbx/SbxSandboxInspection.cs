using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Sbx;

/// <summary>Looks inside a sandbox through <c>sbx exec</c>, which starts a stopped sandbox.</summary>
public sealed class SbxSandboxInspection(IProcessRunner runner) : ISandboxInspection
{
    private readonly SbxInvoker _invoker = new(runner);

    public async Task<bool> HasProgramAsync(
        string sandbox,
        string program,
        CancellationToken cancellationToken
    )
    {
        var result = await _invoker.RunAsync(
            new ProcessRequest(
                _invoker.ExecutableName,
                ["exec", sandbox, "sh", "-c", "command -v \"$1\"", "_", program]
            ),
            cancellationToken
        );

        return result.Succeeded;
    }

    public async Task<string?> GetWorkspaceDirectoryAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var result = await _invoker.RunAsync(
            new ProcessRequest(
                _invoker.ExecutableName,
                ["exec", sandbox, "sh", "-c", "printf %s \"$WORKSPACE_DIR\""]
            ),
            cancellationToken
        );

        var directory = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(directory) ? null : directory;
    }
}
