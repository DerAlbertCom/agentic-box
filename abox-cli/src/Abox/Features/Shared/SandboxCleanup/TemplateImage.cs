namespace Abox.Features.Shared.SandboxCleanup;

/// <summary>A template image the local sandbox runtime holds.</summary>
/// <param name="Repository">The registry path the image is stored in.</param>
/// <param name="Tag">The tag of the image.</param>
/// <param name="Id">The image id the runtime reports.</param>
/// <param name="CreatedAt">When the image was built, when the runtime reports it.</param>
public sealed record TemplateImage(
    string Repository,
    string Tag,
    string Id,
    DateTimeOffset? CreatedAt
)
{
    /// <summary>What names the image for removal: repository and tag.</summary>
    public string Reference => $"{Repository}:{Tag}";
}
