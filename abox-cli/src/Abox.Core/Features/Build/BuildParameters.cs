namespace Abox.Features.Build;

/// <summary>
/// The parameters of <c>abox build</c>: the teams to build, the full path to search their
/// sources from and the validated tag.
/// </summary>
internal sealed record BuildParameters(
    IReadOnlyList<string> Teams,
    string RootDirectory,
    string Tag,
    bool Verify,
    bool IncludePersonalConfigurations,
    bool MetadataOnly
);
