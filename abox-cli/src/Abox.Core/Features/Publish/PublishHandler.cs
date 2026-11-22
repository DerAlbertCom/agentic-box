using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Publish;

/// <summary>
/// Runs <c>abox publish</c>: resolves the teams to their squads and publishes them, without
/// preflight.
/// </summary>
internal sealed class PublishHandler(SquadResolver resolver, PublishService publish)
    : ICommandHandler<PublishParameters>
{
    public async Task<int> HandleAsync(
        PublishParameters parameters,
        CancellationToken cancellationToken
    )
    {
        return await publish.PublishAsync(
            new PublishRequest(
                resolver.ResolveTeams(parameters.RootDirectory, parameters.Teams),
                parameters.Tag,
                parameters.Builder
            ),
            cancellationToken
        );
    }
}
