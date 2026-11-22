using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Publish;

internal sealed class PublishHandler(SquadResolver resolver, PublishService publish)
    : ICommandHandler<PublishParameters>
{
    public async Task<int> HandleAsync(
        PublishParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var resolved = parameters
            .Teams.Select(team => resolver.ResolveTeam(parameters.RootDirectory, team))
            .ToList();
        return await publish.PublishAsync(
            new PublishRequest(resolved, parameters.Tag, parameters.Builder),
            cancellationToken
        );
    }
}
