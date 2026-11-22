namespace Abox.Features.Shared.SandboxCleanup;

/// <summary>A template image the local sandbox runtime holds.</summary>
/// <param name="Reference">What names the image for removal and in reports.</param>
/// <param name="Kind">What the image is to abox.</param>
/// <param name="Set">The assembled kit set the image belongs to; empty unless it is part of one.</param>
/// <param name="Id">The image id the runtime reports.</param>
/// <param name="CreatedAt">When the image was built, when the runtime reports it.</param>
public sealed record TemplateImage(
    string Reference,
    TemplateKind Kind,
    string Set,
    string Id,
    DateTimeOffset? CreatedAt
);

/// <summary>What a template image is to abox.</summary>
public enum TemplateKind
{
    /// <summary>An image abox did not build, such as another repository's.</summary>
    Unrelated,

    /// <summary>Part of an assembled kit set: the set itself, one of its kits or its alias.</summary>
    AssembledSet,

    /// <summary>A built kit that assembled sets are made from.</summary>
    KitSource,
}
