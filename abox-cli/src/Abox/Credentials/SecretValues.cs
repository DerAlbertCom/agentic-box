using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Credentials;

/// <summary>
/// The values of the services on the host: asked for as the <c>secrets</c> section of
/// the squad declares, and kept by <c>sbx secret</c>.
/// </summary>
public sealed class SecretValues(ISbxClient sbx, IConsole console)
{
    /// <summary>
    /// Whether the value of a service is on the host. A service sbx does not know is
    /// stored as a custom secret that is sent to all its hosts under its variable.
    /// </summary>
    public static bool IsStored(string name, SecretDeclaration? declaration, HostSecrets stored)
    {
        var secret = declaration ?? new SecretDeclaration(name);
        return secret.IsCustom
            ? stored.Custom.Any(custom => Covers(custom, secret))
            : stored.Services.Contains(secret.Service);
    }

    /// <summary>
    /// The stored custom secret that shares a host or the variable of a custom secret
    /// without being its binding, so storing the secret could break the other one.
    /// </summary>
    public static CustomSecret? Conflict(SecretDeclaration declaration, HostSecrets stored)
    {
        if (!declaration.IsCustom || stored.Custom.Any(custom => Covers(custom, declaration)))
        {
            return null;
        }

        var hosts = declaration.Hosts ?? [];
        return stored.Custom.FirstOrDefault(custom =>
            custom.Targets.Any(hosts.Contains)
            || declaration.EnvironmentVariable is not null
                && custom.EnvironmentVariable == declaration.EnvironmentVariable
        );
    }

    /// <summary>Why a custom secret is not stored over a conflicting one.</summary>
    public static string DescribeConflict(SecretDeclaration declaration, CustomSecret conflict)
    {
        var targets = conflict.Targets.Count > 0 ? string.Join(", ", conflict.Targets) : "no host";
        var variable = conflict.EnvironmentVariable ?? "no variable";
        return $"it conflicts with the stored custom secret for {targets} ({variable}). "
            + $"Another sandbox may need that binding: review it with 'sbx secret ls' and remove it yourself before you set '{declaration.Service}'.";
    }

    private static bool Covers(CustomSecret custom, SecretDeclaration declaration) =>
        (declaration.Hosts ?? []).All(custom.Targets.Contains)
        && (
            declaration.EnvironmentVariable is null
            || custom.EnvironmentVariable == declaration.EnvironmentVariable
        );

    /// <summary>
    /// Asks for the value of a secret: one value, or the parts it is built from. Returns
    /// null when there is nothing to read.
    /// </summary>
    public string? Ask(SecretDeclaration declaration)
    {
        var parts = new List<string>();

        if (declaration.Inputs is { Count: > 0 })
        {
            foreach (var input in declaration.Inputs)
            {
                var prompt = input.Prompt ?? $"The {input.Name} of '{declaration.Service}'";
                var part = console.ReadSecret($"{prompt}: ");
                if (part is null)
                {
                    return null;
                }

                parts.Add(part);
            }
        }
        else
        {
            var prompt = declaration.Prompt ?? $"The secret '{declaration.Service}'";
            var value = console.ReadSecret($"{prompt}: ");
            if (value is null)
            {
                return null;
            }

            parts.Add(value);
        }

        var joined = string.Join(declaration.Separator, parts);

        return declaration.Encoding == SecretEncoding.Base64
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(joined))
            : joined;
    }

    /// <summary>
    /// Asks for the value of a service and stores it: a custom secret for a service sbx
    /// does not know. A secret with a command asks nothing; sbx runs the command. A custom
    /// secret is not stored over a conflicting one.
    /// </summary>
    public async Task<SecretSetting> SetAsync(
        SecretDeclaration declaration,
        HostSecrets stored,
        CancellationToken cancellationToken
    )
    {
        if (Conflict(declaration, stored) is { } conflict)
        {
            return new SecretSetting(true, false, DescribeConflict(declaration, conflict));
        }

        if (declaration.Command is not null)
        {
            console.WriteLine(
                $"The secret '{declaration.Service}' comes from the host command: {declaration.Command}"
            );
            return SecretSetting.From(
                await sbx.SetCustomSecretAsync(declaration, null, cancellationToken)
            );
        }

        var value = Ask(declaration);
        if (string.IsNullOrEmpty(value))
        {
            return SecretSetting.NotGiven;
        }

        var outcome = declaration.IsCustom
            ? await sbx.SetCustomSecretAsync(declaration, value, cancellationToken)
            : await sbx.SetSecretAsync(declaration.Service, value, cancellationToken);
        return SecretSetting.From(outcome);
    }
}

/// <summary>How setting a secret went.</summary>
/// <param name="Given">Whether a value was given, a command run or a conflict found.</param>
/// <param name="Stored">Whether the host keeps the value now.</param>
/// <param name="Output">What sbx said.</param>
public sealed record SecretSetting(bool Given, bool Stored, string Output)
{
    /// <summary>Nothing was given, so nothing was stored.</summary>
    public static SecretSetting NotGiven { get; } = new(false, false, string.Empty);

    /// <summary>The result of storing a value with sbx.</summary>
    public static SecretSetting From(SbxOutcome outcome) =>
        new(true, outcome.Succeeded, outcome.Output.Trim());
}
