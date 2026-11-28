using Abox.Features.Shared.Common;
using Abox.Features.Shared.GitHubTokens;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// Gets the secrets of a squad ready before its sandbox is created, from what the build
/// recorded in <c>squad.json</c>, so no team sources are needed. sbx rejects a sandbox whose
/// required credential is missing, and kits use theirs while they install, so this runs
/// once, after the replacement question and before anything is created or replaced. When
/// something is missing it opens the guided secrets setup on a terminal; without one it
/// stops on a missing required secret and warns about a missing optional one.
/// </summary>
internal sealed class CreateSecretsCheck(
    SecretsSetup setup,
    GitHubTokenExpiries tokenExpiries,
    IConsole console
)
{
    /// <summary>
    /// Checks every secret of the squad and sets up what is missing. Fails with an
    /// <see cref="AboxException"/> when a required secret is still missing.
    /// </summary>
    /// <param name="squad">The secrets of the squad, as the build recorded them.</param>
    /// <param name="squadName">The squad the sandbox is created from.</param>
    /// <param name="location">The folder the sandbox is created for.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    public async Task PrepareAsync(
        SquadSecrets squad,
        string squadName,
        string location,
        CancellationToken cancellationToken
    )
    {
        tokenExpiries.WarnWhenClose();
        if (squad.Services.Count == 0)
        {
            return;
        }

        IReadOnlyList<SecretState> missing;
        try
        {
            missing = SecretsSetup.Missing(await setup.InspectAsync(squad, cancellationToken));
        }
        catch (AboxException exception)
        {
            console.WriteError(
                $"Warning: the secrets of the squad '{squadName}' were not checked: {exception.Message}"
            );
            return;
        }

        if (missing.Count == 0)
        {
            return;
        }

        var init = $"abox secrets init --location \"{location}\"";
        if (!console.IsInteractive)
        {
            StopOrWarn(missing, squadName, init);
            return;
        }

        var outcome = await setup.RunAsync(
            squad,
            new SecretsSetupPass(
                missing,
                $"The secrets setup has started: the squad '{squadName}' needs {Count(missing.Count)} that {(missing.Count == 1 ? "is" : "are")} not stored or approved.",
                StoredValues.Keep
            ),
            cancellationToken
        );
        if (outcome.NotReady.Count > 0)
        {
            throw new AboxException(
                $"The sandbox was not created: the required {Names(outcome.NotReady)} {(outcome.NotReady.Count == 1 ? "is" : "are")} not stored or approved. "
                    + $"Set {(outcome.NotReady.Count == 1 ? "it" : "them")} up with: {init}. An existing sandbox was not replaced."
            );
        }
    }

    /// <summary>
    /// Without a terminal nothing can be asked: a missing required secret stops the create,
    /// a missing optional one is warned about.
    /// </summary>
    private void StopOrWarn(IReadOnlyList<SecretState> missing, string squadName, string init)
    {
        var lines = missing.Select(SecretsSetup.Line);
        if (missing.Any(state => state.Required))
        {
            throw new AboxException(
                string.Join(
                    Environment.NewLine,
                    [
                        $"The squad '{squadName}' needs secrets that are not stored or approved, and without a terminal abox cannot ask for them:",
                        .. lines,
                        $"Set them up on a terminal with: {init}",
                        "Nothing was created, and an existing sandbox was not replaced.",
                    ]
                )
            );
        }

        console.WriteError(
            string.Join(
                Environment.NewLine,
                [
                    $"Warning: optional secrets of the squad '{squadName}' are not stored or approved; the sandbox starts without them:",
                    .. lines,
                    $"Set them up on a terminal with: {init}",
                ]
            )
        );
    }

    private static string Count(int count) => count == 1 ? "1 secret" : $"{count} secrets";

    private static string Names(IReadOnlyList<string> services) =>
        (services.Count == 1 ? "secret " : "secrets ")
        + string.Join(", ", services.Select(service => $"'{service}'"));
}
