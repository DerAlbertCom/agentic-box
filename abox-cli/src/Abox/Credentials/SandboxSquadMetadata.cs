using System.Text.Json;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Credentials;

/// <summary>
/// What the squad metadata of a sandbox says about its credentials: the services its
/// staged kits ask for, and how the squad declares their values in <c>squad.json</c>.
/// </summary>
public sealed class SandboxSquadMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = true,
    };

    private SandboxSquadMetadata(
        IReadOnlyList<CredentialRequest> requests,
        IReadOnlyList<SecretDeclaration> secrets,
        bool hasDeclarations
    )
    {
        Requests = requests;
        Secrets = secrets;
        HasDeclarations = hasDeclarations;
    }

    /// <summary>The credentials the staged kits ask for, one per service.</summary>
    public IReadOnlyList<CredentialRequest> Requests { get; }

    /// <summary>The secret declarations the squad bundled; empty for an older sandbox.</summary>
    public IReadOnlyList<SecretDeclaration> Secrets { get; }

    /// <summary>
    /// Whether the sandbox carries the squad's secret declarations. A sandbox from an older
    /// build or publication has no <c>squad.json</c> or no <c>Secrets</c> in it.
    /// </summary>
    public bool HasDeclarations { get; }

    /// <summary>The declaration of a service, when the squad has one.</summary>
    public SecretDeclaration? Declaration(string service) =>
        Secrets.FirstOrDefault(secret => secret.Service == service);

    /// <summary>The request of a service, when a staged kit asks for it.</summary>
    public CredentialRequest? Request(string service) =>
        Requests.FirstOrDefault(request => request.Service == service);

    /// <summary>
    /// Reads the raw squad files of a sandbox. A malformed descriptor or <c>Secrets</c> field
    /// fails with a message naming the sandbox.
    /// </summary>
    public static SandboxSquadMetadata Read(string sandbox, SandboxSquadFiles files)
    {
        var requests = CredentialRequest.Merge(
            files.Kits.Select(kit =>
                (
                    kit.Kit,
                    KitDescriptor.Parse(
                        kit.Yaml,
                        $"{sandbox}:/usr/share/sandbox/kit/{kit.Kit}/kit.yaml"
                    )
                )
            )
        );
        var secrets = files.SquadJson is null ? null : ReadSecrets(sandbox, files.SquadJson);
        return new SandboxSquadMetadata(requests, secrets ?? [], secrets is not null);
    }

    private static IReadOnlyList<SecretDeclaration>? ReadSecrets(string sandbox, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (
                document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Secrets", out var element)
                || element.ValueKind == JsonValueKind.Null
            )
            {
                return null;
            }

            var entries =
                element.Deserialize<List<SquadMetadataSecret?>>(JsonOptions)
                ?? throw new JsonException("Secrets is not a list.");
            var secrets = entries
                .Select(entry =>
                    (
                        entry ?? throw new AboxException("a secret entry needs a 'Service'")
                    ).ToDeclaration()
                )
                .ToList();
            var duplicate = secrets
                .GroupBy(secret => secret.Service, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
            {
                throw new AboxException($"the secret '{duplicate.Key}' is declared twice");
            }

            return secrets;
        }
        catch (Exception exception) when (exception is JsonException or AboxException)
        {
            throw new AboxException(
                $"The squad metadata of the sandbox '{sandbox}' (/usr/share/abox/squad.json) has no valid 'Secrets': {exception.Message} Rebuild or republish the squad, then recreate the sandbox.",
                exception
            );
        }
    }
}
