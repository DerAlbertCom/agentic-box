namespace Abox.Features.Build;

/// <summary>
/// The parameters of <c>abox build</c>: the teams to build, the full path to search their
/// sources from, the validated tag and whether every component image is built again.
/// </summary>
internal sealed record BuildParameters(
    IReadOnlyList<string> Teams,
    string RootDirectory,
    string Tag,
    bool Rebuild
);
