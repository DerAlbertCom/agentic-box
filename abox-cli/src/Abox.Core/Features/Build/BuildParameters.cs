namespace Abox.Features.Build;

/// <summary>
/// The parameters of <c>abox build</c>: the teams to build, the given directory to search
/// their sources from (null when none is given), the validated tag and whether every
/// component image is built again.
/// </summary>
internal sealed record BuildParameters(
    IReadOnlyList<string> Teams,
    string? Root,
    string Tag,
    bool Rebuild
);
