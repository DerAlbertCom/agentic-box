using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Build;

internal sealed class BuildHandler(Preflight preflight, SquadResolver resolver, BuildService build)
    : ICommandHandler<BuildParameters>
{
    public async Task<int> HandleAsync(
        BuildParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var request = new BuildRequest(
            parameters
                .Teams.Select(team => resolver.ResolveTeam(parameters.RootDirectory, team))
                .ToList(),
            parameters.RootDirectory,
            parameters.Tag,
            parameters.Verify,
            parameters.IncludePersonalConfigurations,
            parameters.MetadataOnly
        );

        if (!request.MetadataOnly && !await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        return await build.BuildAsync(request, cancellationToken);
    }
}
