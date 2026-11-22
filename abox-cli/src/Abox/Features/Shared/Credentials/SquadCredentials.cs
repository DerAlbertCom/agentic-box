using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// Gets the credentials the local kits of a squad ask for ready before a sandbox is
/// created: abox asks for missing values and approvals itself, so sbx never has to.
/// Of the kits' requests only the <c>apiKey</c> mechanism is checked; sbx notes the others
/// itself. The custom secrets the squad declares are checked too, so a kit's setup that
/// reads one from its variable does not fail on a value that was never set.
/// </summary>
public sealed class SquadCredentials(
    SquadResolver resolver,
    PersonalConfigurations personalConfigurations,
    ISbxClient sbx,
    SbxCredentialBindings bindings,
    SecretValues values,
    IConsole console
)
{
    /// <summary>
    /// Checks that every credential has a stored value and an approval. On a terminal it
    /// asks for what is missing; without one it warns. It never fails the create: sbx
    /// starts without an optional credential and rejects a required one itself.
    /// </summary>
    /// <param name="rootDirectory">The project that holds the squad's sources; null skips the check.</param>
    /// <param name="team">The team the squad belongs to.</param>
    /// <param name="squad">The squad the sandbox is created from.</param>
    /// <param name="location">The folder the sandbox is created for.</param>
    public async Task PrepareAsync(
        string? rootDirectory,
        string team,
        string squad,
        string location,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return;
        }

        try
        {
            var resolved = resolver.Resolve(
                rootDirectory,
                team,
                squad,
                personalConfigurations.Files
            );
            var requested = CredentialRequest.Of(resolved);
            var requests = requested.Where(request => request.Domains.Count > 0).ToList();
            var custom = resolved
                .Secrets.Where(secret =>
                    secret.IsCustom && requested.All(request => request.Service != secret.Service)
                )
                .ToList();
            if (requests.Count == 0 && custom.Count == 0)
            {
                return;
            }

            var stored = await sbx.ListSecretsAsync(cancellationToken);
            var missing = requests
                .Select(request => Check(request, resolved.Secrets, stored))
                .Concat(custom.Select(secret => CheckCustom(secret, stored)))
                .Where(state => !state.Stored || !state.Approved)
                .ToList();
            if (missing.Count == 0)
            {
                return;
            }

            if (console.IsInteractive)
            {
                await AskAsync(missing, stored, cancellationToken);
            }
            else
            {
                Warn(missing, location);
            }
        }
        catch (AboxException exception)
        {
            console.WriteError(
                $"Warning: the credentials of the squad '{squad}' were not checked: {exception.Message}"
            );
        }
    }

    private CredentialState Check(
        CredentialRequest request,
        IReadOnlyList<SecretDeclaration> secrets,
        HostSecrets stored
    )
    {
        var declaration = secrets.FirstOrDefault(secret => secret.Service == request.Service);
        return new CredentialState(
            request,
            declaration,
            stored.Holds(declaration ?? new SecretDeclaration(request.Service)),
            bindings.Covers(request.Service, request.Domains)
        );
    }

    /// <summary>
    /// A custom secret the squad declares but no kit asks for with <c>credential@1</c>,
    /// such as one a kit's setup reads from its variable. sbx sends it without approval.
    /// </summary>
    private static CredentialState CheckCustom(SecretDeclaration secret, HostSecrets stored) =>
        new(
            new CredentialRequest(secret.Service, [], secret.Hosts ?? [], Optional: false),
            secret,
            stored.Holds(secret),
            Approved: true,
            ApprovalNeeded: false
        );

    private async Task AskAsync(
        IReadOnlyList<CredentialState> missing,
        HostSecrets stored,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine("The kits of the squad ask for credentials that are not ready:");
        foreach (var state in missing)
        {
            console.WriteLine(state.Describe());
        }

        console.WriteLine();

        foreach (var state in missing)
        {
            var name = state.Request.Service;
            var ready =
                state.Stored
                || state.Declaration is { } declaration
                    && console.Confirm($"Set the secret '{name}' now?")
                    && await SetAsync(declaration, stored, cancellationToken);

            if (!ready)
            {
                console.WriteLine($"The sandbox starts without the credential '{name}'.");
                continue;
            }

            if (!state.Approved)
            {
                var domains = string.Join(", ", state.Request.Domains);
                if (console.Confirm($"Approve sending the credential '{name}' to {domains}?"))
                {
                    bindings.Approve(name, state.Request.Domains);
                    console.WriteLine($"Approved the credential '{name}' for {domains}.");
                }
                else
                {
                    console.WriteLine($"The sandbox starts without the credential '{name}'.");
                }
            }
        }
    }

    private async Task<bool> SetAsync(
        SecretDeclaration declaration,
        HostSecrets stored,
        CancellationToken cancellationToken
    )
    {
        var name = declaration.Service;
        var setting = await values.SetAsync(declaration, stored, cancellationToken);
        switch (setting.Result)
        {
            case SecretSettingResult.Skipped:
                console.WriteLine($"The secret '{name}' was not set.");
                return false;
            case SecretSettingResult.Refused or SecretSettingResult.Failed:
                console.WriteError($"The secret '{name}' was not stored: {setting.Message}");
                return false;
            default:
                console.WriteLine($"Stored the secret '{name}'.");
                return true;
        }
    }

    private void Warn(IReadOnlyList<CredentialState> missing, string location)
    {
        console.WriteError(
            "Warning: the kits of the squad ask for credentials that are not ready:"
        );
        foreach (var state in missing)
        {
            console.WriteError(state.Describe());
        }

        foreach (
            var state in missing.Where(state => !state.Stored && state.Declaration is not null)
        )
        {
            console.WriteError(
                $"Set the value with: abox secrets set --location \"{location}\" --service {state.Request.Service}"
            );
        }

        if (missing.Any(state => !state.Approved))
        {
            console.WriteError(
                "An approval is asked for only on a terminal: run abox create on one to approve. The sandbox starts without the credentials that are not approved."
            );
        }
    }

    private sealed record CredentialState(
        CredentialRequest Request,
        SecretDeclaration? Declaration,
        bool Stored,
        bool Approved,
        bool ApprovalNeeded = true
    )
    {
        public string Describe()
        {
            var value =
                Stored ? "value stored"
                : Declaration is null ? "value not set (not declared in 'secrets')"
                : "value not set";
            var approval =
                !ApprovalNeeded ? ""
                : Approved ? ", approved"
                : ", not approved";
            var source = Request.Kits.Count > 0 ? string.Join(", ", Request.Kits) : "secrets";
            var required = Request.Optional ? "" : ", required";
            var domains =
                Request.Domains.Count > 0 ? $" for {string.Join(", ", Request.Domains)}" : "";
            return $"  {Request.Service} ({source}{required}){domains}: {value}{approval}";
        }
    }
}
