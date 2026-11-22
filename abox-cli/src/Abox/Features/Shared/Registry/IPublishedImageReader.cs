namespace Abox.Features.Shared.Registry;

/// <summary>Reads published images from the registry without running them.</summary>
public interface IPublishedImageReader
{
    /// <summary>Whether the image tooling is installed on the host.</summary>
    bool IsInstalled { get; }

    /// <summary>The digest a tagged reference currently points to, unvalidated.</summary>
    Task<string?> ResolveDigestAsync(string reference, CancellationToken cancellationToken);

    Task<RegistryManifest> InspectAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Downloads the image for <paramref name="platform"/> and saves it as an image archive.</summary>
    Task SaveAsync(
        string reference,
        string platform,
        string archive,
        CancellationToken cancellationToken
    );
}
