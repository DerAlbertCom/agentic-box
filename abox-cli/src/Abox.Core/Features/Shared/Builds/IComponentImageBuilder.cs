namespace Abox.Features.Shared.Builds;

/// <summary>A component image to build from its kit directory into a host image store.</summary>
/// <param name="Directory">The kit directory the image is built from.</param>
/// <param name="Descriptor">The kit's build recipe.</param>
/// <param name="Reference">The reference the image gets: its final registry name and tag.</param>
/// <param name="ImageStore">The host image store the image is built into.</param>
/// <param name="WorkDirectory">A directory for the builder's scratch files.</param>
/// <param name="WithGitHubToken">Whether the recipe may mount the host's GitHub token.</param>
public sealed record ComponentImageBuild(
    string Directory,
    string Descriptor,
    string Reference,
    string ImageStore,
    string WorkDirectory,
    bool WithGitHubToken
);

/// <summary>
/// Builds component images (workload and mixins) for <c>linux/amd64</c> into the host's
/// image store, which <c>abox create --local</c> uses and <c>abox publish</c> pushes.
/// </summary>
public interface IComponentImageBuilder
{
    /// <summary>Checks that the host can build into its current image store, and returns that store.</summary>
    Task<string> HostImageStoreAsync(CancellationToken cancellationToken);

    /// <summary>Builds the image, loads it into the image store and returns its manifest digest.</summary>
    Task<string> BuildAsync(ComponentImageBuild build, CancellationToken cancellationToken);
}
