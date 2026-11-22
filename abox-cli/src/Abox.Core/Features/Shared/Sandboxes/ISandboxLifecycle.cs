namespace Abox.Features.Shared.Sandboxes;

/// <summary>Lists, creates and removes the sandboxes of the local sandbox runtime.</summary>
public interface ISandboxLifecycle
{
    /// <summary>The sandboxes the runtime knows about. Discovery errors throw.</summary>
    Task<IReadOnlyList<SandboxInfo>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a sandbox without asking anything: nothing is read from the terminal while it
    /// runs. A refusal of the runtime is returned, not thrown.
    /// </summary>
    Task<SandboxCreation> CreateAsync(
        SandboxDefinition definition,
        CancellationToken cancellationToken
    );

    /// <summary>Removes a sandbox without asking. A refusal of the runtime is returned, not thrown.</summary>
    Task<SandboxRemoval> RemoveAsync(string name, CancellationToken cancellationToken);
}

/// <summary>How a sandbox creation ended.</summary>
/// <param name="Output">What the runtime wrote, with known secrets redacted, so notes and failures can be shown.</param>
/// <param name="Failure">The few lines that name the cause of a failure; null when the sandbox was created.</param>
public sealed record SandboxCreation(string Output, string? Failure = null)
{
    public bool Succeeded => Failure is null;
}

/// <summary>Whether a sandbox was removed.</summary>
/// <param name="Removed">Whether the runtime removed the sandbox.</param>
/// <param name="Output">What the runtime wrote, so a refusal can be shown.</param>
public sealed record SandboxRemoval(bool Removed, string Output);
