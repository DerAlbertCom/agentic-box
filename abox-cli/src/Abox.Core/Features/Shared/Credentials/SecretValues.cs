using Abox.Features.Shared.Common;
using Abox.Features.Shared.GitHubTokens;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// The values of the services on the host: asked for as the <c>secrets</c> section of
/// the squad declares, and kept by the host secret store.
/// </summary>
internal sealed class SecretValues(
    IHostSecretStore store,
    IConsole console,
    GitHubTokenSignIn gitHubTokens
)
{
    /// <summary>Why a custom secret is not stored over a conflicting one.</summary>
    public static string DescribeConflict(SecretDeclaration declaration, CustomSecret conflict)
    {
        var targets = conflict.Targets.Count > 0 ? string.Join(", ", conflict.Targets) : "no host";
        var variable = conflict.EnvironmentVariable ?? "no variable";
        return $"it conflicts with the stored custom secret for {targets} ({variable}). "
            + $"Another sandbox may need that binding: review it with 'sbx secret ls' and remove it yourself before you set '{declaration.Service}'.";
    }

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
    /// <param name="declaration">The secret; a withheld command is never run.</param>
    /// <param name="stored">The secrets the host keeps.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    public async Task<SecretSetting> SetAsync(
        SecretDeclaration declaration,
        HostSecrets stored,
        CancellationToken cancellationToken
    )
    {
        if (stored.ConflictWith(declaration) is { } conflict)
        {
            return new SecretSetting(
                SecretSettingResult.Refused,
                DescribeConflict(declaration, conflict)
            );
        }

        if (declaration.Command is { Line: { } command })
        {
            console.WriteLine(
                $"The secret '{declaration.Service}' comes from the host command: {command}"
            );
            return SecretSetting.From(
                await store.SetCustomAsync(declaration, null, cancellationToken)
            );
        }

        if (declaration.Command is not null)
        {
            throw new ArgumentException(
                $"The command of the secret '{declaration.Service}' is withheld; abox never runs it.",
                nameof(declaration)
            );
        }

        var value = GitHubTokenSignIn.Handles(declaration)
            ? await gitHubTokens.ObtainAsync(declaration, cancellationToken)
            : Ask(declaration);
        if (string.IsNullOrEmpty(value))
        {
            return SecretSetting.Skipped;
        }

        var outcome = declaration.IsCustom
            ? await store.SetCustomAsync(declaration, value, cancellationToken)
            : await store.SetAsync(declaration.Service, value, cancellationToken);
        return SecretSetting.From(outcome);
    }
}

/// <summary>How setting a secret ended.</summary>
internal enum SecretSettingResult
{
    /// <summary>The host keeps the value now.</summary>
    Stored,

    /// <summary>No value was given, so nothing was stored.</summary>
    Skipped,

    /// <summary>A conflicting binding is stored, so the secret was not stored over it.</summary>
    Refused,

    /// <summary>The host store did not store the value.</summary>
    Failed,
}

/// <summary>How setting a secret went.</summary>
/// <param name="Result">How it ended.</param>
/// <param name="Message">Why it was refused, or what the host store said.</param>
internal sealed record SecretSetting(SecretSettingResult Result, string Message)
{
    /// <summary>Nothing was given, so nothing was stored.</summary>
    public static SecretSetting Skipped { get; } = new(SecretSettingResult.Skipped, string.Empty);

    /// <summary>The result of storing a value in the host store.</summary>
    public static SecretSetting From(HostSecretChange change) =>
        new(
            change.Succeeded ? SecretSettingResult.Stored : SecretSettingResult.Failed,
            change.Message
        );
}
