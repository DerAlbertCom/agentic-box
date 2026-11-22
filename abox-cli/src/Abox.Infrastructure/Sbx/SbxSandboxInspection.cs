using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Sbx;

/// <summary>Looks inside a sandbox through <c>sbx exec</c>, which starts a stopped sandbox.</summary>
internal sealed class SbxSandboxInspection(SbxInvoker invoker) : ISandboxInspection
{
    public async Task<bool> HasProgramAsync(
        string sandbox,
        string program,
        CancellationToken cancellationToken
    )
    {
        var result = await invoker.RunAsync(
            invoker.Call(["exec", sandbox, "sh", "-c", "command -v \"$1\"", "_", program]),
            cancellationToken
        );

        return result.Succeeded;
    }

    public async Task<string?> GetWorkspaceDirectoryAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var result = await invoker.RunAsync(
            invoker.Call(["exec", sandbox, "sh", "-c", "printf %s \"$WORKSPACE_DIR\""]),
            cancellationToken
        );

        var directory = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(directory) ? null : directory;
    }
}
