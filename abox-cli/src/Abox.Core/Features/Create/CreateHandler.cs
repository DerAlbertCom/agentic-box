using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Create;

internal sealed class CreateHandler(
    Preflight preflight,
    SquadResolver resolver,
    SandboxCreator create
) : ICommandHandler<CreateParameters>
{
    public async Task<int> HandleAsync(
        CreateParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var location = parameters.Location;
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        var selectedSquad = folderConfig?.SelectSquad(parameters.Squad) ?? parameters.Squad;
        if (!parameters.Local && string.IsNullOrWhiteSpace(selectedSquad))
        {
            throw new AboxException(
                $"Remote create requires --squad <name> or 'squad' in {FolderConfig.FileNames}; published squad discovery is not supported. No local team files are used."
            );
        }

        var resolved = parameters.Local
            ? resolver.ResolveTeam(parameters.SourceRoot, parameters.Team)
            : new SquadResolver.ResolvedTeam(parameters.Team, string.Empty, []);

        var request = new SandboxCreationRequest(
            resolved,
            selectedSquad,
            location.FullPath,
            parameters.Tag,
            folderConfig,
            parameters.AssumeYes,
            Local: parameters.Local
        );

        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        return await create.CreateAsync(request, cancellationToken);
    }
}
