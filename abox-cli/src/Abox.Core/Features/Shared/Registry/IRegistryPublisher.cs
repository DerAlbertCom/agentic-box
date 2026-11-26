namespace Abox.Features.Shared.Registry;

/// <summary>
/// An image to build from a kit directory and push. <paramref name="WorkDirectory"/> holds
/// the publisher's scratch files; <paramref name="Builder"/> is the user's optional builder.
/// </summary>
public sealed record RegistryPush(
    string Directory,
    string Descriptor,
    string Reference,
    string WorkDirectory,
    string? Builder
);

/// <summary>Pushes images to the registry, builds and pushes sets, inspects them and moves tags.</summary>
public interface IRegistryPublisher
{
    /// <summary>Whether the image tooling is installed on the host.</summary>
    bool IsInstalled { get; }

    /// <summary>Fails unless images can be built and pushed.</summary>
    Task RequireReadyAsync(CancellationToken cancellationToken);

    /// <summary>Builds and pushes the image; returns the pushed digest, or null when it cannot be read.</summary>
    Task<string?> PushAsync(RegistryPush push, CancellationToken cancellationToken);

    /// <summary>
    /// Pushes an image of the host image store unchanged, so the registry keeps its digest.
    /// </summary>
    Task PushImageAsync(string imageStore, string reference, CancellationToken cancellationToken);

    /// <summary>Points <paramref name="reference"/> at the already pushed <paramref name="candidate"/>.</summary>
    Task TagAsync(
        string candidate,
        string reference,
        string? builder,
        CancellationToken cancellationToken
    );

    Task<RegistryManifest> InspectAsync(
        string reference,
        string? builder,
        CancellationToken cancellationToken
    );
}
