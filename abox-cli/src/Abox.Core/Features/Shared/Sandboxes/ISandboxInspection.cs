namespace Abox.Features.Shared.Sandboxes;

/// <summary>Looks inside a sandbox, starting it when it is stopped.</summary>
public interface ISandboxInspection
{
    /// <summary>Whether a program exists inside the sandbox.</summary>
    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    /// <summary>Where the runtime put the workspace of the sandbox, when it says.</summary>
    Task<string?> GetWorkspaceDirectoryAsync(string sandbox, CancellationToken cancellationToken);
}
