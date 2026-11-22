namespace Abox.Features.Shared.SandboxCleanup;

/// <summary>Whether a template image was removed.</summary>
/// <param name="Removed">Whether the runtime removed the image.</param>
/// <param name="Refusal">Why the runtime kept the image, for the user; empty when removed.</param>
public sealed record TemplateRemoval(bool Removed, string Refusal)
{
    public static TemplateRemoval Done { get; } = new(true, string.Empty);
}
