namespace Abox.Features.Shared.SandboxCleanup;

/// <summary>
/// Cleans up what the local sandbox runtime holds: lists sandboxes and template images,
/// stops sandboxes and removes template images.
/// </summary>
public interface ISandboxCleanup
{
    /// <summary>The sandboxes the runtime knows about.</summary>
    Task<IReadOnlyList<SandboxSummary>> ListSandboxesAsync(CancellationToken cancellationToken);

    /// <summary>Stops one sandbox without removing it; false when the runtime could not.</summary>
    Task<bool> StopSandboxAsync(string name, CancellationToken cancellationToken);

    /// <summary>The template images the runtime holds.</summary>
    Task<IReadOnlyList<TemplateImage>> ListTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>Removes one template image, without asking.</summary>
    Task<TemplateRemoval> RemoveTemplateAsync(
        TemplateImage image,
        CancellationToken cancellationToken
    );
}
