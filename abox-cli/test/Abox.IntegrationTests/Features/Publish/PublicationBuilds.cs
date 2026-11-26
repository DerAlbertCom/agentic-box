using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;

namespace Abox.Tests;

/// <summary>Writes explicitly selected publication inputs before capturing their provenance.</summary>
internal static class PublicationBuilds
{
    internal static string SaveWorkload(
        BuildStore store,
        BuildManifest manifest,
        string descriptorPath,
        string descriptor,
        string team = "sample"
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);
        File.WriteAllText(descriptorPath, descriptor);
        SaveVerified(store, manifest, team: team);
        return descriptorPath;
    }

    /// <summary>Writes the build's configuration mixin, as the build does, and saves the manifest.</summary>
    internal static void SaveVerified(
        BuildStore store,
        BuildManifest manifest,
        IReadOnlyList<SecretDeclaration>? secrets = null,
        string team = "sample"
    )
    {
        var directory = SquadConfigurationMixin.Generate(
            team,
            manifest,
            secrets ?? [],
            Path.Combine(
                store.DirectoryFor(new SquadBuildIdentity(team, manifest.Squad, manifest.Tag)),
                SquadConfigurationMixin.MixinName
            )
        );
        // The configuration image of the build bundles the configuration just written.
        if (manifest.Component("config") is { } image)
        {
            image.Directory = directory;
            LocalWorkloadFactory.Load(image.Reference, directory);
        }
        manifest.SourceHashes = BuildSources.Capture(manifest);
        store.Save(manifest);
    }
}
