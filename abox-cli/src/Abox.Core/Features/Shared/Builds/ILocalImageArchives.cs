namespace Abox.Features.Shared.Builds;

/// <summary>Saves component images of the host image store as image archives, to read their files.</summary>
public interface ILocalImageArchives
{
    /// <summary>Saves a local image as an image archive, without pulling it.</summary>
    Task SaveAsync(
        string imageStore,
        string reference,
        string archive,
        CancellationToken cancellationToken
    );
}
