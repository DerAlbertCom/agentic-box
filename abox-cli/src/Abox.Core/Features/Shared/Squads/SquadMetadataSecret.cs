using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.Squads;

/// <summary>
/// A secret declaration as the <c>Secrets</c> field of <c>squad.json</c> holds it: how to
/// ask for a service's value, never a value or a host command; a command-backed secret is
/// only marked. The build writes it and <c>abox secrets</c> reads it back from a sandbox,
/// so the fields are nullable for reading and checked by <see cref="ToDeclaration"/>.
/// An older <c>squad.json</c> has no description, no documentation path and no input flags:
/// its inputs are secret.
/// </summary>
internal sealed record SquadMetadataSecret(
    string? Service,
    string? Prompt,
    IReadOnlyList<string?>? Hosts,
    string? Env,
    IReadOnlyList<SquadMetadataSecretInput?>? Inputs,
    string? Separator,
    string? Encoding,
    string? Placeholder,
    bool FromCommand,
    string? Description = null,
    string? Docs = null,
    string? ValueCommand = null
)
{
    /// <summary>The entry of a declaration; its host command stays out.</summary>
    public static SquadMetadataSecret From(SecretDeclaration declaration) =>
        new(
            declaration.Service,
            declaration.Prompt,
            declaration.Hosts ?? [],
            declaration.EnvironmentVariable,
            (declaration.Inputs ?? [])
                .Select(input => new SquadMetadataSecretInput(
                    input.Name,
                    input.Prompt,
                    input.Secret,
                    input.Value
                ))
                .ToList(),
            declaration.Separator,
            declaration.Encoding.ToString().ToLowerInvariant(),
            declaration.Placeholder,
            declaration.Command is not null,
            declaration.Description,
            declaration.Docs,
            declaration.ValueCommand
        );

    /// <summary>
    /// The declaration of the entry; a command-backed one has a withheld command. Fails with
    /// an <see cref="AboxException"/> for an entry that is not a valid declaration.
    /// </summary>
    public SecretDeclaration ToDeclaration()
    {
        if (string.IsNullOrWhiteSpace(Service))
        {
            throw new AboxException("a secret entry needs a 'Service'");
        }

        var inputs = (Inputs ?? [])
            .Select(input =>
                input is null || string.IsNullOrWhiteSpace(input.Name)
                    ? throw new AboxException($"an input of the secret '{Service}' needs a 'Name'")
                    : new SecretInput(input.Name, input.Prompt, input.Secret ?? true, input.Value)
            )
            .ToList();
        var hosts = (Hosts ?? [])
            .Select(host =>
                string.IsNullOrWhiteSpace(host)
                    ? throw new AboxException($"a host of the secret '{Service}' is empty")
                    : host
            )
            .ToList();
        if (FromCommand && hosts.Count == 0)
        {
            throw new AboxException(
                $"the secret '{Service}' comes from a host command, so it needs 'Hosts'"
            );
        }

        return new SecretDeclaration(
            Service,
            Prompt,
            inputs,
            Separator ?? ":",
            SecretDeclaration.ParseEncoding(Encoding, "squad.json"),
            hosts.Count > 0 ? hosts : null,
            string.IsNullOrWhiteSpace(Env) ? null : Env,
            FromCommand ? HostCommand.Withheld : null,
            string.IsNullOrWhiteSpace(Placeholder) ? null : Placeholder,
            string.IsNullOrWhiteSpace(Description) ? null : Description,
            string.IsNullOrWhiteSpace(Docs) ? null : Docs,
            string.IsNullOrWhiteSpace(ValueCommand) ? null : ValueCommand,
            (Inputs ?? []).Any(input => input is { Secret: null })
        );
    }
}

/// <summary>
/// A part of a secret as <c>squad.json</c> holds it. A missing <c>Secret</c> means secret.
/// </summary>
internal sealed record SquadMetadataSecretInput(
    string? Name,
    string? Prompt,
    bool? Secret = null,
    string? Value = null
);
