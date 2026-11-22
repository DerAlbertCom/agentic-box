using System.Text.Json;

namespace Abox.Features.Shared.Registry;

/// <summary>
/// A registry manifest: an image with its retained v3 kit descriptor, or an index of
/// platform images when <see cref="Platforms"/> is set.
/// </summary>
public sealed record RegistryManifest(
    JsonElement? KitDescriptor,
    IReadOnlyList<PlatformManifest>? Platforms
)
{
    /// <summary>The image's v3 kit descriptor of the given kind; fails with <paramref name="error"/> otherwise.</summary>
    public JsonElement Descriptor(string kind, string error)
    {
        if (
            KitDescriptor is not { } descriptor
            || descriptor.GetProperty("schemaVersion").GetString() != "3"
            || descriptor.GetProperty("kind").GetString() != kind
        )
        {
            throw new JsonException(error);
        }
        return descriptor;
    }
}

/// <summary>One entry of an image index.</summary>
public sealed record PlatformManifest(
    string? Digest,
    string? Os,
    string? Architecture,
    bool IsAttestation = false
);
