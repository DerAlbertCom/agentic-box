namespace Abox.Features.Shared.Builds;

/// <summary>Inspects workload images in the host's image store.</summary>
public interface ILocalWorkloadImageStore
{
    /// <summary>Whether the image tooling is installed on the host.</summary>
    bool IsInstalled { get; }

    /// <summary>Fails unless the image store's only builder shares that store.</summary>
    Task RequireHostBuilderAsync(string imageStore, CancellationToken cancellationToken);

    /// <summary>The content ID of a local image, which changes whenever the image is replaced.</summary>
    Task<string> ImageIdAsync(
        string imageStore,
        string reference,
        CancellationToken cancellationToken
    );
}
