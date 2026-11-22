namespace Abox.Features.Publish;

/// <summary>
/// The parameters of <c>abox publish</c>: the teams to publish, the directory to search
/// their sources from, the tag (the publish service validates it) and the Buildx builder.
/// <see cref="RootDirectory"/> stays as given, unlike the full path of <c>abox build</c>,
/// because a missing team is reported with it.
/// </summary>
internal sealed record PublishParameters(
    IReadOnlyList<string> Teams,
    string RootDirectory,
    string Tag,
    string? Builder
);
