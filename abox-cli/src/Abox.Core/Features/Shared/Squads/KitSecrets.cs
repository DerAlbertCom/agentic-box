using System.Text.Json;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Squads;

/// <summary>
/// The secrets a local kit declares in the <c>secrets.yaml</c> next to its kit descriptor:
/// how the values of the services it asks for are obtained, and its custom secrets. A
/// service's description is the <c>description</c> of its <c>credential@1</c> capability.
/// </summary>
internal static class KitSecrets
{
    /// <summary>The file a kit declares its secrets in.</summary>
    public const string FileName = "secrets.yaml";

    private static readonly string[] DeclarationKeys =
    [
        "service",
        "prompt",
        "description",
        "docs",
        "inputs",
        "separator",
        "encoding",
        "host",
        "env",
        "command",
        "placeholder",
        "valueCommand",
    ];

    /// <summary>
    /// The secrets of the local kits of a squad, one per service. Several kits may declare
    /// a service only identically.
    /// </summary>
    public static IReadOnlyList<SecretDeclaration> Collect(
        IReadOnlyList<KitSource> kits,
        IYamlParser parser
    )
    {
        var secrets = new List<SecretDeclaration>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kit in kits.Where(kit => kit.IsLocal))
        {
            foreach (var secret in Load(kit.Directory!, parser))
            {
                Add(secrets, owners, kit.Name, secret);
            }
        }

        return secrets;
    }

    /// <summary>Adds the declaration of a kit, unless another kit declared it identically.</summary>
    private static void Add(
        List<SecretDeclaration> secrets,
        Dictionary<string, string> owners,
        string kit,
        SecretDeclaration secret
    )
    {
        if (owners.TryAdd(secret.Service, kit))
        {
            secrets.Add(secret);
            return;
        }

        var known = secrets.First(declared => declared.Service == secret.Service);
        if (!Same(known, secret))
        {
            throw new AboxException(
                $"The kits '{owners[secret.Service]}' and '{kit}' declare the secret '{secret.Service}' differently. "
                    + "Within a squad a service is declared by one kit, or identically by several."
            );
        }
    }

    /// <summary>
    /// The secrets one kit declares; none without a <c>secrets.yaml</c>. A declaration that
    /// is not a custom secret must name a service a <c>credential@1</c> capability of the kit
    /// asks for.
    /// </summary>
    public static IReadOnlyList<SecretDeclaration> Load(string kitDirectory, IYamlParser parser)
    {
        var path = Path.Combine(kitDirectory, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        var services = KitDescriptor.Load(kitDirectory, parser)?.Services ?? [];
        var secrets = new List<SecretDeclaration>();
        foreach (var entry in parser.ReadListFile(path))
        {
            var secret = Read(entry, path);
            if (secrets.Any(declared => declared.Service == secret.Service))
            {
                throw new AboxException(
                    $"'{path}': the secret '{secret.Service}' is declared twice."
                );
            }

            secrets.Add(Describe(secret, services, path));
        }

        return secrets;
    }

    private static SecretDeclaration Describe(
        SecretDeclaration secret,
        IReadOnlyList<KitService> services,
        string path
    )
    {
        if (secret.IsCustom)
        {
            return secret;
        }

        if (secret.Description is not null)
        {
            throw new AboxException(
                $"'{path}': the secret '{secret.Service}' takes its description from the 'description' of its credential@1 capability, so it has no 'description'."
            );
        }

        var service =
            services.FirstOrDefault(service => service.Service == secret.Service)
            ?? throw new AboxException(
                $"'{path}': no credential@1 capability of the kit asks for the service '{secret.Service}'. "
                    + "Fix the name, or declare a custom secret with a 'host' or an 'env'."
            );
        return secret with
        {
            Description = string.IsNullOrWhiteSpace(service.Description)
                ? null
                : service.Description.Trim(),
        };
    }

    /// <summary>Reads one declaration: a service name, or a mapping.</summary>
    private static SecretDeclaration Read(YamlEntry entry, string source)
    {
        if (entry.IsScalar)
        {
            return new SecretDeclaration(entry.ScalarValue);
        }

        entry.RejectUnknownKeys(DeclarationKeys);

        var service =
            entry.GetString("service")
            ?? throw new AboxException($"'{source}': a secret entry needs a 'service'.");
        var hosts = entry.GetStrings("host");
        var command = entry.GetString("command");
        var placeholder = entry.GetString("placeholder");
        if ((command is not null || placeholder is not null) && hosts.Count == 0)
        {
            throw new AboxException(
                $"'{source}': the secret '{service}' needs a 'host' for its 'command' or 'placeholder'."
            );
        }

        var valueCommand = entry.GetString("valueCommand");
        var asked = entry.Has("inputs") || entry.Has("prompt");
        if (valueCommand is not null && (asked || command is not null))
        {
            throw new AboxException(
                $"'{source}': the secret '{service}' takes its value from the output of its 'valueCommand', so it has no 'prompt', 'inputs' or 'command'."
            );
        }

        if (command is not null && asked)
        {
            throw new AboxException(
                $"'{source}': the secret '{service}' takes its value from its 'command', so it has no 'prompt' or 'inputs'."
            );
        }

        return new SecretDeclaration(
            service,
            entry.GetString("prompt"),
            entry.GetEntries("inputs").Select(input => ReadInput(input, service, source)).ToList(),
            entry.GetString("separator") ?? ":",
            SecretDeclaration.ParseEncoding(entry.GetString("encoding"), source),
            hosts.Count > 0 ? hosts : null,
            entry.GetString("env"),
            command is null ? null : new HostCommand(command),
            placeholder,
            entry.GetString("description"),
            entry.GetString("docs"),
            valueCommand
        );
    }

    private static SecretInput ReadInput(YamlEntry input, string service, string source)
    {
        input.RejectUnknownKeys("name", "prompt", "secret", "value");
        var name =
            input.GetString("name")
            ?? throw new AboxException($"'{source}': a secret input needs a 'name'.");
        var value = input.GetString("value");
        if (value is not null && (input.Has("prompt") || input.Has("secret")))
        {
            throw new AboxException(
                $"'{source}': the input '{name}' of the secret '{service}' has a fixed 'value', so it is never asked and has no 'prompt' or 'secret'."
            );
        }

        return new SecretInput(
            name,
            input.GetString("prompt"),
            input.GetBoolean("secret") ?? true,
            value
        );
    }

    /// <summary>
    /// Whether two kits declare a service the same way. The description comes from each
    /// kit's capability, so it is left out.
    /// </summary>
    private static bool Same(SecretDeclaration left, SecretDeclaration right) =>
        Fingerprint(left) == Fingerprint(right);

    private static string Fingerprint(SecretDeclaration secret) =>
        JsonSerializer.Serialize(SquadMetadataSecret.From(secret with { Description = null }))
        + "\n"
        + secret.Command?.Line;
}
