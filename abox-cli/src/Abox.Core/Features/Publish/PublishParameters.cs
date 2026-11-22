namespace Abox.Features.Publish;

/// <summary>
/// The parameters of <c>abox publish</c>: the teams to publish, the directory to search
/// their sources from, the tag (the publish service validates it) and the Buildx builder.
/// </summary>
internal sealed record PublishParameters(
    IReadOnlyList<string> Teams,
    string RootDirectory,
    string Tag,
    string? Builder
);
