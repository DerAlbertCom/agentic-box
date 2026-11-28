using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

/// <summary>
/// Runs <c>abox secrets init</c>: the guided secrets setup for everything a squad needs,
/// before a sandbox exists, from a local build or a published set, without team sources.
/// It asks for what is not stored or not approved; with <c>--all</c> for everything again.
/// </summary>
internal sealed class SecretsInitHandler(
    Preflight preflight,
    SquadSecretsReader reader,
    SecretsSetup setup,
    IConsole console
) : ICommandHandler<SecretsInitParameters>
{
    public async Task<int> HandleAsync(
        SecretsInitParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var folderConfig = await parameters.Location.FolderConfigurationAsync(cancellationToken);
        var squadName =
            folderConfig?.SelectSquad(parameters.Squad)
            ?? parameters.Squad
            ?? throw new AboxException(
                $"Init requires --squad <name> or 'squad' in {FolderConfig.FileNames}."
            );
        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        var identity = new SquadBuildIdentity(parameters.Team, squadName, parameters.Tag);
        var squad = parameters.Local
            ? reader.ReadLocal(identity)
            : await reader.ReadPublishedAsync(identity, cancellationToken);
        var states = await setup.InspectAsync(squad, cancellationToken);
        var selected = parameters.All
            ? states.Where(state => state.Applies).ToList()
            : SecretsSetup.Missing(states);
        if (selected.Count == 0)
        {
            console.WriteLine(
                $"Every secret of the squad '{squadName}' is stored and approved; there is nothing to set up."
            );
            await setup.WriteTableAsync(squad, cancellationToken);
            return 0;
        }

        var location = parameters.Location.FullPath;
        if (!console.IsInteractive)
        {
            setup.RefuseWithoutTerminal("abox secrets init", selected, location);
            return 1;
        }

        var introduction = parameters.All
            ? $"The secrets setup has started: each of the {selected.Count} secrets of the squad '{squadName}' is asked for again."
            : $"The secrets setup has started: the squad '{squadName}' needs {selected.Count} secret{(selected.Count == 1 ? "" : "s")} that {(selected.Count == 1 ? "is" : "are")} not stored or approved.";
        var outcome = await setup.RunAsync(
            squad,
            new SecretsSetupPass(
                selected,
                introduction,
                parameters.All ? StoredValues.AskAgain : StoredValues.Keep
            ),
            cancellationToken
        );
        return outcome.Succeeded ? 0 : 1;
    }
}
