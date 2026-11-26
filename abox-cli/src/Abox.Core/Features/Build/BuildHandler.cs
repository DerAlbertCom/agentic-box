using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Build;

/// <summary>
/// Runs <c>abox build</c>: finds the team sources, resolves the teams to their squads, runs
/// preflight, then builds.
/// </summary>
internal sealed class BuildHandler(
    Preflight preflight,
    SourceRootResolver sourceRoots,
    SquadResolver resolver,
    BuildService build
) : ICommandHandler<BuildParameters>
{
    public async Task<int> HandleAsync(
        BuildParameters parameters,
        CancellationToken cancellationToken
    )
    {
        // The team sources are first needed here, so this is where sourceRoot is asked for.
        var root = Path.GetFullPath(
            sourceRoots.Resolve(parameters.Root, Directory.GetCurrentDirectory(), parameters.Teams)
        );
        var request = new BuildRequest(
            resolver.ResolveTeams(root, parameters.Teams),
            root,
            parameters.Tag,
            parameters.Rebuild
        );

        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        return await build.BuildAsync(request, cancellationToken);
    }
}
