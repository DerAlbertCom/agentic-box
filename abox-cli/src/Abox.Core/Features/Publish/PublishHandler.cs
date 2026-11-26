using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Publish;

/// <summary>
/// Runs <c>abox publish</c>: resolves the teams to their squads and publishes them, without
/// preflight.
/// </summary>
internal sealed class PublishHandler(
    SourceRootResolver sourceRoots,
    SquadResolver resolver,
    PublishService publish
) : ICommandHandler<PublishParameters>
{
    public async Task<int> HandleAsync(
        PublishParameters parameters,
        CancellationToken cancellationToken
    )
    {
        // A given root stays as given, because a missing team is reported with it.
        var root = sourceRoots.Resolve(
            parameters.Root,
            Directory.GetCurrentDirectory(),
            parameters.Teams
        );
        return await publish.PublishAsync(
            new PublishRequest(
                resolver.ResolveTeams(root, parameters.Teams),
                parameters.Tag,
                parameters.Builder
            ),
            cancellationToken
        );
    }
}
