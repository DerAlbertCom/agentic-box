namespace Abox.Features.Shared.Builds;

/// <summary>A workload image to build from its kit directory into a host image store.</summary>
/// <param name="Directory">The kit directory the image is built from.</param>
/// <param name="Descriptor">The kit's build recipe.</param>
/// <param name="Reference">The reference the image gets.</param>
/// <param name="ImageStore">The host image store the image is built into.</param>
public sealed record WorkloadImageBuild(
    string Directory,
    string Descriptor,
    string Reference,
    string ImageStore
);

/// <summary>Builds workload images into the host's image store, which sandboxes create from.</summary>
public interface IWorkloadImageBuilder
{
    /// <summary>Checks that the host can build into its current image store, and returns that store.</summary>
    Task<string> HostImageStoreAsync(CancellationToken cancellationToken);

    /// <summary>A new, unique reference for a workload image in the host image store.</summary>
    string NewImageReference();

    /// <summary>Builds the image and loads it into the image store.</summary>
    Task BuildAsync(WorkloadImageBuild build, CancellationToken cancellationToken);
}
