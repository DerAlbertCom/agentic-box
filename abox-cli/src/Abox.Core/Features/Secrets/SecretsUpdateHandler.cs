using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Secrets;

/// <summary>
/// Runs <c>abox secrets update</c>: the guided secrets setup for the services the user picks
/// from a list with their state, or names with <c>--service</c>. The services are those of
/// the folder's sandbox, so the list reflects what it runs; without a sandbox, those of the
/// squad of the folder configuration, from its published set.
/// </summary>
internal sealed class SecretsUpdateHandler(
    Preflight preflight,
    FolderSandbox sandboxes,
    ISandboxSquadFileReader squadFiles,
    SquadSecretsReader reader,
    SecretsSetup setup,
    IYamlParser parser,
    IConsole console
) : ICommandHandler<SecretsUpdateParameters>
{
    public async Task<int> HandleAsync(
        SecretsUpdateParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var folderConfig = await parameters.Location.FolderConfigurationAsync(cancellationToken);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
        {
            return 1;
        }

        var location = parameters.Location.FullPath;
        var squad = await ReadAsync(parameters, folderConfig, cancellationToken);
        var states = (await setup.InspectAsync(squad, cancellationToken))
            .Where(state => state.Applies)
            .ToList();
        var chosen =
            parameters.Services.Count > 0 ? Named(parameters.Services, squad, states) : null;
        if (chosen is null && parameters.Services.Count > 0)
        {
            return 1;
        }

        if (!console.IsInteractive)
        {
            setup.RefuseWithoutTerminal("abox secrets update", chosen ?? states, location);
            return 1;
        }

        chosen ??= Pick(states);
        if (chosen.Count == 0)
        {
            console.WriteLine("No secret was chosen; nothing changed.");
            return 0;
        }

        var outcome = await setup.RunAsync(
            squad,
            new SecretsSetupPass(
                chosen,
                $"The secrets setup has started for {chosen.Count} chosen secret{(chosen.Count == 1 ? "" : "s")}.",
                StoredValues.AskAgain
            ),
            cancellationToken
        );
        return outcome.Succeeded ? 0 : 1;
    }

    private async Task<SquadSecrets> ReadAsync(
        SecretsUpdateParameters parameters,
        FolderConfig? folderConfig,
        CancellationToken cancellationToken
    )
    {
        var sandbox = await sandboxes.FindAsync(parameters.Location.FullPath, cancellationToken);
        if (sandbox is not null)
        {
            var files = await squadFiles.ReadAsync(sandbox.Name, cancellationToken);
            return SquadSecrets.Read(sandbox.Name, files, parser);
        }

        var team =
            parameters.Team
            ?? throw new AboxException(
                $"There is no sandbox for '{parameters.Location.FullPath}', and no team names its squad. Set 'team' in {FolderConfig.FileNames} or 'defaultTeam' in the user configuration, or run abox secrets init --team <team> --squad <squad>."
            );
        var squad =
            folderConfig?.SelectSquad(null)
            ?? throw new AboxException(
                $"There is no sandbox for '{parameters.Location.FullPath}', and {FolderConfig.FileNames} names no squad. Run abox secrets init --squad <squad> instead."
            );
        return await reader.ReadPublishedAsync(
            new SquadBuildIdentity(team, squad, parameters.Tag),
            cancellationToken
        );
    }

    /// <summary>The named services, or null after reporting one the squad does not know.</summary>
    private List<SecretState>? Named(
        IReadOnlyList<string> services,
        SquadSecrets squad,
        IReadOnlyList<SecretState> states
    )
    {
        var chosen = new List<SecretState>();
        foreach (var name in services.Distinct(StringComparer.Ordinal))
        {
            var state = states.FirstOrDefault(state => state.Service == name);
            if (state is null)
            {
                console.WriteError(
                    $"The service '{name}' is not declared and no kit of the squad asks for it. Known services: {string.Join(", ", squad.Services)}."
                );
                return null;
            }

            chosen.Add(state);
        }

        return chosen;
    }

    private List<SecretState> Pick(IReadOnlyList<SecretState> states)
    {
        var picks = console.ChooseMany(
            "Which secrets do you want to set up?",
            states.Select(state => $"{state.Service} ({state.Summary})").ToList()
        );
        return (picks ?? []).Select(index => states[index]).ToList();
    }
}
