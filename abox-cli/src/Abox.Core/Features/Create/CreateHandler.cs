using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Create;

/// <summary>
/// Runs <c>abox create</c>: selects the squad, runs preflight, then creates the sandbox. A
/// published set and a local build both carry everything needed, so no team sources are read.
/// </summary>
internal sealed class CreateHandler(Preflight preflight, SandboxCreator create, IConsole console)
    : ICommandHandler<CreateParameters>
{
    public async Task<int> HandleAsync(
        CreateParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var location = parameters.Location;
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        var selectedSquad = folderConfig?.SelectSquad(parameters.Squad) ?? parameters.Squad;
        if (string.IsNullOrWhiteSpace(selectedSquad))
        {
            throw new AboxException(
                $"Create requires --squad <name> or 'squad' in {FolderConfig.FileNames}; squad discovery is not supported. No team sources are used."
            );
        }

        var resolved = new SquadResolver.ResolvedTeam(parameters.Team, string.Empty, []);

        var request = new SandboxCreationRequest(
            resolved,
            selectedSquad,
            location.FullPath,
            parameters.Tag,
            folderConfig,
            parameters.AssumeYes,
            Local: parameters.Local,
            Memory: parameters.Memory,
            Cpus: parameters.Cpus
        );

        console.WriteLine("Checking sbx...");
        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        return await create.CreateAsync(request, cancellationToken);
    }
}
