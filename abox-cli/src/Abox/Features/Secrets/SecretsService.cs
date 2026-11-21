using Abox.Configuration;
using Abox.Credentials;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sandboxes;
using Abox.Sbx;

namespace Abox.Features.Secrets;

/// <summary>What to do with the secrets of a folder's sandbox.</summary>
/// <param name="Location">The folder whose sandbox is meant.</param>
/// <param name="FolderConfig">The folder configuration, for the create-recovery hint.</param>
/// <param name="Services">The services to act on. All declared ones when the list is empty.</param>
public sealed record SecretsRequest(
    string Location,
    FolderConfig? FolderConfig,
    IReadOnlyList<string> Services
);

/// <summary>
/// The secrets of the host for a folder's sandbox: its staged kits ask for a service, its
/// bundled squad metadata says how the value of that service is obtained, and sbx keeps
/// the value globally.
/// </summary>
public sealed class SecretsService(
    FolderSandbox sandboxes,
    ISbxClient sbx,
    SecretValues values,
    IConsole console
)
{
    /// <summary>Lists the services of the sandbox and whether they are stored.</summary>
    public async Task<int> ListAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var metadata = await ReadAsync(request, cancellationToken);
        var stored = await sbx.ListSecretsAsync(cancellationToken);

        var names = metadata
            .Requests.Select(service => service.Service)
            .Union(metadata.Secrets.Select(secret => secret.Service))
            .Order(StringComparer.Ordinal)
            .ToList();

        console.WriteLine(
            $"{"SERVICE".PadRight(20)} {"KITS".PadRight(20)} {"NEED".PadRight(9)} {"STATE".PadRight(10)} NOTE"
        );

        foreach (var name in names)
        {
            var asked = metadata.Request(name);
            var kits = asked is null ? "-" : string.Join(", ", asked.Kits);
            var need =
                asked is null ? "-"
                : asked.Optional ? "optional"
                : "required";
            var declaration = metadata.Declaration(name);
            var state = SecretValues.IsStored(name, declaration, stored) ? "stored" : "not set";
            var note = declaration is null ? "not declared" : "";

            console.WriteLine(
                $"{name.PadRight(20)} {kits.PadRight(20)} {need.PadRight(9)} {state.PadRight(10)} {note}".TrimEnd()
            );
        }

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

            var setting = await values.SetAsync(
                declaration ?? new SecretDeclaration(name),
                cancellationToken
            );
            if (!setting.Given)
            {
                console.WriteError($"The service '{name}' was not set.");
                failed = true;
                continue;
            }

            if (!setting.Stored)
            {
                console.WriteError($"The secret '{name}' was not stored: {setting.Output}");
                failed = true;
                continue;
            }

            console.WriteLine($"Stored the secret '{name}'.");
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
            var outcome = await sbx.RemoveSecretAsync(
                metadata.Declaration(name) ?? new SecretDeclaration(name),
                cancellationToken
            );
            if (!outcome.Succeeded)
            {
                console.WriteError($"The secret '{name}' was not removed: {outcome.Output.Trim()}");
                failed = true;
                continue;
            }

            console.WriteLine($"Removed the secret '{name}'.");
        }

        return failed ? 1 : 0;
    }

    private static IReadOnlyList<string> Wanted(
        SecretsRequest request,
        SandboxSquadMetadata metadata
    ) =>
        request.Services.Count > 0
            ? request.Services.Distinct(StringComparer.Ordinal).ToList()
            : metadata.Secrets.Select(secret => secret.Service).ToList();

    private async Task<SandboxSquadMetadata> ReadAsync(
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
        var files = await sbx.ReadSquadFilesAsync(sandbox.Name, cancellationToken);
        return SandboxSquadMetadata.Read(sandbox.Name, files);
    }

    private void WriteOlderSandboxHint(SandboxSquadMetadata metadata)
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
