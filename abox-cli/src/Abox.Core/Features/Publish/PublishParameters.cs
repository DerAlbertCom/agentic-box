namespace Abox.Features.Publish;

/// <summary>
/// The parameters of <c>abox publish</c>: the teams to publish, the given directory to
/// search their sources from (null when none is given), the tag (the publish service
/// validates it) and the Buildx builder.
/// </summary>
internal sealed record PublishParameters(
    IReadOnlyList<string> Teams,
    string? Root,
    string Tag,
    string? Builder
);
