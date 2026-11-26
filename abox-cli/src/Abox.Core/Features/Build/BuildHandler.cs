using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Build;

/// <summary>
/// Runs <c>abox build</c>: resolves the teams to their squads, runs preflight, then builds.
/// </summary>
internal sealed class BuildHandler(Preflight preflight, SquadResolver resolver, BuildService build)
    : ICommandHandler<BuildParameters>
{
    public async Task<int> HandleAsync(
        BuildParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var request = new BuildRequest(
            resolver.ResolveTeams(parameters.RootDirectory, parameters.Teams),
            parameters.RootDirectory,
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
