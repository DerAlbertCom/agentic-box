using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.GitHubTokens;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Secrets;

/// <summary>What to do with the secrets of a folder's sandbox.</summary>
/// <param name="Location">The folder whose sandbox is meant.</param>
/// <param name="FolderConfig">The folder configuration, for the create-recovery hint.</param>
/// <param name="Services">The services to act on. All declared ones when the list is empty.</param>
internal sealed record SecretsRequest(
    string Location,
    FolderConfig? FolderConfig,
    IReadOnlyList<string> Services
);

/// <summary>
/// The secrets of the host for a folder's sandbox: its staged kits ask for a service, its
/// bundled squad metadata says how the value of that service is obtained, and sbx keeps
/// the value globally.
/// </summary>
internal sealed class SecretsService(
    FolderSandbox sandboxes,
    ISandboxSquadFileReader squadFiles,
    IHostSecretStore store,
    SecretValues values,
    GitHubTokenExpiries tokenExpiries,
    RememberedInputs remembered,
    IConsole console,
    IYamlParser parser
)
{
    /// <summary>Lists the services of the sandbox and whether they are stored.</summary>
    public async Task<int> ListAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var metadata = await ReadAsync(request, cancellationToken);
        SecretsTable.Write(console, metadata, await store.ListAsync(cancellationToken));
        WriteOlderSandboxHint(metadata);
        return 0;
    }

    /// <summary>Asks for the values of the services and stores them on the host.</summary>
    public async Task<int> SetAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var metadata = await ReadAsync(request, cancellationToken);
        var wanted = Wanted(request, metadata);

        if (wanted.Count == 0)
        {
            console.WriteError("There are no secrets to set.");
            WriteOlderSandboxHint(metadata);
            return 1;
        }

        var stored = await store.ListAsync(cancellationToken);
        var failed = false;
        var unknown = false;

        foreach (var name in wanted)
        {
            var asked = metadata.Request(name);
            var declaration = metadata.Declaration(name);
            if (declaration is null && asked is null)
            {
                console.WriteError(
                    $"The service '{name}' is not declared and no kit of the sandbox asks for it, so abox cannot tell how to store it."
                );
                unknown = true;
                failed = true;
                continue;
            }

            if (declaration is { IsCustom: false } && asked is null)
            {
                console.WriteLine($"Note: no kit of the sandbox asks for the service '{name}'.");
            }

            if (declaration?.Command is { Line: null })
            {
                failed |= !values.ReportWithheldCommand(declaration, stored);
                continue;
            }

            var setting = await values.SetAsync(
                declaration ?? new SecretDeclaration(name),
                asked?.Domains ?? [],
                stored,
                cancellationToken
            );
            switch (setting.Result)
            {
                case SecretSettingResult.Skipped:
                    console.WriteError($"The service '{name}' was not set.");
                    failed = true;
                    break;
                case SecretSettingResult.Refused or SecretSettingResult.Failed:
                    console.WriteError($"The secret '{name}' was not stored: {setting.Message}");
                    failed = true;
                    break;
                default:
                    console.WriteLine($"Stored the secret '{name}'.");
                    break;
            }
        }

        if (unknown)
        {
            WriteOlderSandboxHint(metadata);
        }

        return failed ? 1 : 0;
    }

    /// <summary>Removes the secrets of the services from the host.</summary>
    public async Task<int> RemoveAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var metadata = await ReadAsync(request, cancellationToken);
        var wanted = Wanted(request, metadata);

        if (wanted.Count == 0)
        {
            console.WriteError("There are no secrets to remove.");
            WriteOlderSandboxHint(metadata);
            return 1;
        }

        var failed = false;

        foreach (var name in wanted)
        {
            var outcome = await store.RemoveAsync(
                SecretIdentity.Of(metadata.Declaration(name) ?? new SecretDeclaration(name)),
                cancellationToken
            );
            if (!outcome.Succeeded)
            {
                console.WriteError($"The secret '{name}' was not removed: {outcome.Message}");
                failed = true;
                continue;
            }

            tokenExpiries.Forget(name);
            remembered.Forget(name);
            console.WriteLine($"Removed the secret '{name}'.");
        }

        return failed ? 1 : 0;
    }

    private static IReadOnlyList<string> Wanted(SecretsRequest request, SquadSecrets metadata) =>
        request.Services.Count > 0
            ? request.Services.Distinct(StringComparer.Ordinal).ToList()
            : metadata.Secrets.Select(secret => secret.Service).ToList();

    private async Task<SquadSecrets> ReadAsync(
        SecretsRequest request,
        CancellationToken cancellationToken
    )
    {
        var sandbox = await sandboxes.RequireAsync(
            request.Location,
            request.FolderConfig,
            "secrets never creates or replaces one",
            cancellationToken
        );
        var files = await squadFiles.ReadAsync(sandbox.Name, cancellationToken);
        return SquadSecrets.Read(sandbox.Name, files, parser);
    }

    private void WriteOlderSandboxHint(SquadSecrets metadata)
    {
        if (metadata.HasDeclarations)
        {
            return;
        }

        console.WriteError(
            "The sandbox carries no secret declarations: it was created from an older build or publication. "
                + "Services its kits ask for can still be set with the default prompt (--service <name>); "
                + "for the squad's prompts and its custom services, recreate the sandbox from a current build or publication."
        );
    }
}
