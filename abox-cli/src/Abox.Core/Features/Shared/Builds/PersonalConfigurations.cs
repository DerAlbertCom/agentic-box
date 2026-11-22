using Abox.Features.Shared.Configuration;

namespace Abox.Features.Shared.Builds;

/// <summary>
/// The personal <c>.abox</c> files of the user. They add to a squad, they never replace it.
/// </summary>
internal sealed class PersonalConfigurations(AboxPaths paths)
{
    /// <summary>The personal configuration files, in a stable order.</summary>
    public IReadOnlyList<string> Files =>
        Directory.Exists(paths.ConfigDirectory)
            ? Directory
                .EnumerateFiles(paths.ConfigDirectory, "*.abox")
                .OrderBy(file => file)
                .ToList()
            : [];
}
