using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>What the guided secrets setup does with a value the host keeps already.</summary>
internal enum StoredValues
{
    /// <summary>The stored value stays; only a missing approval is asked for.</summary>
    Keep,

    /// <summary>The value is asked for again, to replace it.</summary>
    AskAgain,
}

/// <summary>One pass of the guided secrets setup.</summary>
/// <param name="Services">The services to set up, in order.</param>
/// <param name="Introduction">The first line of the header: what starts and why.</param>
/// <param name="Stored">What happens to a value the host keeps already.</param>
internal sealed record SecretsSetupPass(
    IReadOnlyList<SecretState> Services,
    string Introduction,
    StoredValues Stored
);

/// <summary>How the guided secrets setup ended.</summary>
/// <param name="NotReady">The required services that still lack a stored value or an approval.</param>
/// <param name="Failed">Whether a value was refused, not stored, or must be stored by hand.</param>
internal sealed record SecretsSetupOutcome(IReadOnlyList<string> NotReady, bool Failed)
{
    /// <summary>Whether everything went through.</summary>
    public bool Succeeded => NotReady.Count == 0 && !Failed;
}

/// <summary>
/// The guided secrets setup: given where the services of a squad stand and the ones to set
/// up, it shows each service with its description and documentation link, stores the
/// values, asks for the missing approvals and ends with the table <c>abox secrets list</c>
/// prints. <c>abox secrets init</c>, <c>abox secrets update</c> and <c>abox create</c> all
/// use it. The values stay on the host; abox never reads one back.
/// </summary>
internal sealed class SecretsSetup(
    IHostSecretStore store,
    ICredentialApprovals approvals,
    SecretValues values,
    AboxConfig config,
    IConsole console
)
{
    /// <summary>Where every service of the squad stands on the host.</summary>
    public async Task<IReadOnlyList<SecretState>> InspectAsync(
        SquadSecrets squad,
        CancellationToken cancellationToken
    ) => Inspect(squad, await store.ListAsync(cancellationToken));

    private IReadOnlyList<SecretState> Inspect(SquadSecrets squad, HostSecrets stored) =>
        squad
            .Services.Select(service =>
            {
                var request = squad.Request(service);
                var declaration = squad.Declaration(service);
                var identity = declaration ?? new SecretDeclaration(service);
                return new SecretState(
                    service,
                    request,
                    declaration,
                    stored.Holds(identity),
                    request is not { Domains.Count: > 0 }
                        || approvals.Covers(service, request.Domains),
                    stored.ConflictWith(identity)
                );
            })
            .ToList();

    /// <summary>
    /// Walks the services of the pass: for each it asks for the value (unless it is stored
    /// and kept) and then for a missing approval. Ends with the table.
    /// </summary>
    public async Task<SecretsSetupOutcome> RunAsync(
        SquadSecrets squad,
        SecretsSetupPass pass,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine(pass.Introduction);
        console.WriteLine(
            "The values stay on this host; sbx injects them into the sandbox. An empty answer skips an optional secret."
        );

        var stored = await store.ListAsync(cancellationToken);
        var notReady = new List<string>();
        var failed = false;
        foreach (var state in pass.Services)
        {
            cancellationToken.ThrowIfCancellationRequested();
            console.WriteLine();
            Describe(state);
            var (ready, failure) = await SetValueAsync(
                state,
                stored,
                pass.Stored,
                cancellationToken
            );
            failed |= failure;
            ready = ready && Approve(state);
            if (!ready && state.Required)
            {
                notReady.Add(state.Service);
            }
        }

        console.WriteLine();
        SecretsTable.Write(console, squad, await store.ListAsync(cancellationToken));
        return new SecretsSetupOutcome(notReady, failed);
    }

    /// <summary>Prints the table of the squad's services, as <c>abox secrets list</c> does.</summary>
    public async Task WriteTableAsync(SquadSecrets squad, CancellationToken cancellationToken) =>
        SecretsTable.Write(console, squad, await store.ListAsync(cancellationToken));

    /// <summary>
    /// Says that the guided setup needs a terminal and how each service is stored without one.
    /// </summary>
    public void RefuseWithoutTerminal(
        string command,
        IReadOnlyList<SecretState> services,
        string location
    )
    {
        console.WriteError(
            $"{command} asks for values and approvals and needs a terminal. Without one, store each value with:"
        );
        foreach (var state in services)
        {
            console.WriteError(
                $"  abox secrets set --location \"{location}\" --service {state.Service}"
            );
        }
    }

    /// <summary>The services of the squad that need a value or an approval.</summary>
    public static IReadOnlyList<SecretState> Missing(IReadOnlyList<SecretState> states) =>
        states.Where(state => state.Applies && !state.Ready).ToList();

    /// <summary>How one service is described: a line per missing state, for warnings.</summary>
    public static string Line(SecretState state)
    {
        var value = state.Stored ? "value stored" : "value not set";
        var approval =
            !state.ApprovalNeeded ? ""
            : state.Approved ? ", approved"
            : ", not approved";
        var need = state.Required ? "required" : "optional";
        return $"  {state.Service} ({state.Source}, {need}): {value}{approval}";
    }

    private void Describe(SecretState state)
    {
        var need = state.Required ? "required" : "optional";
        console.WriteLine($"{state.Service} ({state.Source}, {need})");
        if (state.Description is { } description)
        {
            console.WriteLine($"  {description}");
        }

        if (state.ApprovalNeeded)
        {
            console.WriteLine($"  Sent to: {string.Join(", ", state.Request!.Domains)}");
        }

        if (DocumentationLink(state.Declaration?.Docs) is { } link)
        {
            console.WriteLine($"  Documentation: {link}");
        }

        if (state.Declaration is { OlderBuild: true })
        {
            console.WriteLine(
                "  The squad was built by an older abox, so every part of this secret is asked as a secret "
                    + "and no value is fixed or remembered. Build or publish the squad again with this abox."
            );
        }
    }

    /// <summary>The documentation page of a repository-relative path, under the configured base.</summary>
    private string? DocumentationLink(string? docs) =>
        string.IsNullOrWhiteSpace(docs)
            ? null
            : $"{config.DocsBaseUrl.TrimEnd('/')}/{docs.Trim().TrimStart('/')}";

    /// <summary>Stores the value of a service. Returns whether it is stored, and whether something failed.</summary>
    private async Task<(bool Ready, bool Failed)> SetValueAsync(
        SecretState state,
        HostSecrets stored,
        StoredValues storedValues,
        CancellationToken cancellationToken
    )
    {
        var name = state.Service;
        var declaration = state.Declaration ?? new SecretDeclaration(name);
        if (state.Conflict is { } conflict)
        {
            console.WriteError(
                $"The secret '{name}' was not stored: {SecretValues.DescribeConflict(declaration, conflict)}"
            );
            return (false, true);
        }

        if (declaration.Command is { Line: null })
        {
            var held = values.ReportWithheldCommand(declaration, stored);
            return (held, !held);
        }

        if (state.Stored && storedValues == StoredValues.Keep)
        {
            console.WriteLine($"The secret '{name}' is stored.");
            return (true, false);
        }

        var setting = await values.SetAsync(
            declaration,
            state.Request?.Domains ?? [],
            stored,
            cancellationToken
        );
        switch (setting.Result)
        {
            case SecretSettingResult.Skipped:
                console.WriteLine(
                    state.Stored
                        ? $"The secret '{name}' was kept as it is."
                        : $"The secret '{name}' was skipped."
                );
                return (state.Stored, false);
            case SecretSettingResult.Refused or SecretSettingResult.Failed:
                console.WriteError($"The secret '{name}' was not stored: {setting.Message}");
                return (false, true);
            default:
                console.WriteLine($"Stored the secret '{name}'.");
                return (true, false);
        }
    }

    /// <summary>Asks for a missing approval. Returns whether the service is approved now.</summary>
    private bool Approve(SecretState state)
    {
        if (state.Approved)
        {
            return true;
        }

        var name = state.Service;
        var domains = state.Request!.Domains;
        var shown = string.Join(", ", domains);
        if (!console.Confirm($"Approve sending the credential '{name}' to {shown}?"))
        {
            console.WriteLine(
                $"The credential '{name}' is not approved; the sandbox starts without it."
            );
            return false;
        }

        approvals.Approve(name, domains);
        console.WriteLine($"Approved the credential '{name}' for {shown}.");
        return true;
    }
}
