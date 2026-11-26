using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Publish;

internal sealed record SelectedPublicationBuild(SquadBuildIdentity Identity, BuildManifest Build);

/// <summary>Loads and validates the build records of the entire selection.</summary>
internal sealed class PublicationSources(BuildStore store)
{
    internal IReadOnlyList<SelectedPublicationBuild> LoadBuilds(PublishRequest request)
    {
        RejectCaseCollisions(request);
        var builds = request
            .Teams.SelectMany(team =>
                team.Squads.Select(squad =>
                    SelectBuild(new SquadBuildIdentity(team.Name, squad, request.Tag))
                )
            )
            .ToList();
        if (builds.Count == 0)
            throw new AboxException("No squad builds were selected for publication.");
        return builds;
    }

    private SelectedPublicationBuild SelectBuild(SquadBuildIdentity identity) =>
        new(identity, LoadBuild(identity));

    private BuildManifest LoadBuild(SquadBuildIdentity identity)
    {
        var (team, squad, tag) = identity;
        _ = identity.Repository;
        var build =
            store.Load(identity)
            ?? throw new AboxException(
                $"No local build of '{squad}:{tag}'. Run 'abox build --team {team} --tag {tag}' first."
            );
        if (!WorkloadFile.IsValidPublishRepository(build.PublishRepository))
            throw new AboxException(
                $"The local build '{squad}:{tag}' has no valid publishRepository. Set 'publishRepository' in its .workload file and run 'abox build --team {team} --tag {tag}' again before publishing."
            );
        foreach (var server in build.Mcp)
            new McpDeclaration(server.Name, server.Url).Validate($"local build {squad}:{tag}");
        if (
            build.ImageStore is null
            || !new[] { ComponentNames.Workload, ComponentNames.Configuration }.All(name =>
                build.Components.Any(component => component.Name == name)
            )
        )
            throw new AboxException(
                $"The local build '{squad}:{tag}' has no component images (an older build). Run 'abox build --team {team} --tag {tag}' before publishing."
            );
        return build;
    }

    private static void RejectCaseCollisions(PublishRequest request)
    {
        if (
            request
                .Teams.Select(team => team.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != request.Teams.Count
            || request.Teams.Any(team =>
                team.Squads.Distinct(StringComparer.OrdinalIgnoreCase).Count() != team.Squads.Count
            )
        )
            throw new AboxException(
                "Selected team or squad names collide when compared without case. Give every team and squad a distinct registry identity."
            );
        foreach (var team in request.Teams)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(team.Folder));
            var duplicateFolder =
                parent is not null
                && Directory.Exists(parent)
                && Directory
                    .EnumerateDirectories(parent)
                    .Count(folder =>
                        Path.GetFileName(folder)
                            .Equals(team.Name, StringComparison.OrdinalIgnoreCase)
                    ) > 1;
            var duplicateSquad =
                Directory.Exists(team.Folder)
                && Directory
                    .EnumerateFiles(team.Folder, "*.abox")
                    .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1);
            if (duplicateFolder || duplicateSquad)
                throw new AboxException(
                    "Team folders or squad files have names that collide without case; rename them before publishing."
                );
        }
    }
}
