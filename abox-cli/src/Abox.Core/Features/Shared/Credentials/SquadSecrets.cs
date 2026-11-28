using System.Text.Json;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// What a squad says about its secrets: the services its kits ask for, and how the squad's
/// kits declare their values. Read from a sandbox, from the <c>squad.json</c> of a local
/// build, or from the metadata of a published set; never from team sources.
/// </summary>
internal sealed class SquadSecrets
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        RespectNullableAnnotations = true,
    };

    /// <param name="requests">The credentials the kits ask for, one per service.</param>
    /// <param name="secrets">The secret declarations of the squad.</param>
    /// <param name="hasDeclarations">Whether the source carries the declarations at all.</param>
    public SquadSecrets(
        IReadOnlyList<CredentialRequest> requests,
        IReadOnlyList<SecretDeclaration> secrets,
        bool hasDeclarations = true
    )
    {
        Requests = requests;
        Secrets = secrets;
        HasDeclarations = hasDeclarations;
    }

    /// <summary>The credentials the kits ask for, one per service.</summary>
    public IReadOnlyList<CredentialRequest> Requests { get; }

    /// <summary>The secret declarations the squad bundled; empty for an older sandbox.</summary>
    public IReadOnlyList<SecretDeclaration> Secrets { get; }

    /// <summary>
    /// Whether the squad metadata carries the secret declarations. A sandbox from an older
    /// build or publication has no <c>squad.json</c> or no <c>Secrets</c> in it.
    /// </summary>
    public bool HasDeclarations { get; }

    /// <summary>Every service a kit asks for or the squad declares, in name order.</summary>
    public IReadOnlyList<string> Services =>
        Requests
            .Select(request => request.Service)
            .Union(Secrets.Select(secret => secret.Service))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The declaration of a service, when the squad has one.</summary>
    public SecretDeclaration? Declaration(string service) =>
        Secrets.FirstOrDefault(secret => secret.Service == service);

    /// <summary>The request of a service, when a kit asks for it.</summary>
    public CredentialRequest? Request(string service) =>
        Requests.FirstOrDefault(request => request.Service == service);

    /// <summary>
    /// Reads the raw squad files of a sandbox. A malformed descriptor or <c>Secrets</c> field
    /// fails with a message naming the sandbox.
    /// </summary>
    public static SquadSecrets Read(string sandbox, SandboxSquadFiles files, IYamlParser parser)
    {
        var requests = CredentialRequest.Merge(
            files.Kits.Select(kit =>
                (
                    kit.Kit,
                    KitDescriptor.Parse(
                        kit.Yaml,
                        $"{sandbox}:/usr/share/sandbox/kit/{kit.Kit}/kit.yaml",
                        parser
                    )
                )
            )
        );
        var secrets = files.SquadJson is null
            ? null
            : ReadSecrets(files.SquadJson, $"The squad metadata of the sandbox '{sandbox}'");
        return new SquadSecrets(requests, secrets ?? [], secrets is not null);
    }

    /// <summary>
    /// Reads the <c>squad.json</c> of a local build: the credentials the build recorded and
    /// the declarations.
    /// </summary>
    /// <param name="json">The text of <c>squad.json</c>.</param>
    /// <param name="source">What the text is, for messages, such as "The local build of 'ac'".</param>
    public static SquadSecrets ReadSquadJson(string json, string source)
    {
        var secrets = ReadSecrets(json, source);
        return new SquadSecrets(ReadCredentials(json, source), secrets ?? [], secrets is not null);
    }

    private static IReadOnlyList<CredentialRequest> ReadCredentials(string json, string source)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (
                !document.RootElement.TryGetProperty("Credentials", out var element)
                || element.ValueKind == JsonValueKind.Null
            )
            {
                return [];
            }

            return (
                element.Deserialize<List<SquadMetadataCredential?>>(JsonOptions)
                ?? throw new JsonException("Credentials is not a list.")
            )
                .Select(entry =>
                    (entry ?? throw new JsonException("A credential entry is null.")).ToRequest()
                )
                .ToList();
        }
        catch (JsonException exception)
        {
            throw Invalid(source, "Credentials", exception);
        }
    }

    private static IReadOnlyList<SecretDeclaration>? ReadSecrets(string json, string source)
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
            throw Invalid(source, "Secrets", exception);
        }
    }

    private static AboxException Invalid(string source, string field, Exception exception) =>
        new(
            $"{source} (/usr/share/abox/squad.json) has no valid '{field}': {exception.Message} Rebuild or republish the squad, then recreate the sandbox.",
            exception
        );
}
