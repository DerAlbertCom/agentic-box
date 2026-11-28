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
    GitHubTokenSignIn gitHubTokens,
    RememberedInputs remembered,
    IHostCommands commands
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
    /// A secret from a host command that the declaration withholds: abox never runs a
    /// command it would take from a sandbox or a published set, so it tells the user how to
    /// store the secret with sbx. Returns whether the secret is stored already.
    /// </summary>
    public bool ReportWithheldCommand(SecretDeclaration declaration, HostSecrets stored)
    {
        var name = declaration.Service;
        if (stored.Holds(declaration))
        {
            console.WriteLine($"The secret '{name}' is stored; it comes from a host command.");
            return true;
        }

        if (stored.ConflictWith(declaration) is { } conflict)
        {
            console.WriteError(
                $"The secret '{name}' was not stored: {DescribeConflict(declaration, conflict)}"
            );
            return false;
        }

        console.WriteError(
            $"The secret '{name}' comes from a host command, which abox never takes from a sandbox. "
                + "Store it with sbx, replacing <host command> with the 'command' of its entry in the kit's 'secrets.yaml' "
                + "(in the team's sources) or the command the team's README gives:"
        );
        console.WriteError(
            "  " + store.ManualCustomSecretInstruction(declaration, "<host command>")
        );
        return false;
    }

    /// <summary>
    /// Asks for the value of a secret: one value, or the parts it is built from. A fixed
    /// part is never asked; a part that is not secret is echoed with its remembered value as
    /// the default; a secret part is masked and its length confirmed. Returns null when an
    /// asked part is empty or there is nothing to read, so nothing incomplete is stored.
    /// </summary>
    private SecretAnswer? Ask(SecretDeclaration declaration)
    {
        var service = declaration.Service;
        IReadOnlyList<SecretInput> inputs = declaration.Inputs is { Count: > 0 } declared
            ? declared
            : [new SecretInput("value", declaration.Prompt ?? $"The secret '{service}'")];
        var parts = new List<string>();
        var remember = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var input in inputs)
        {
            if (input.Value is { } fixedValue)
            {
                parts.Add(fixedValue);
                continue;
            }

            var prompt = $"{input.Prompt ?? $"The {input.Name} of '{service}'"}: ";
            var part = input.Secret
                ? ReadSecret(prompt)
                : console.ReadText(prompt, remembered.Get(service, input.Name));
            if (string.IsNullOrEmpty(part))
            {
                return null;
            }

            if (!input.Secret)
            {
                remember[input.Name] = part;
            }

            parts.Add(part);
        }

        return new SecretAnswer(
            Encode(declaration, string.Join(declaration.Separator, parts)),
            remember
        );
    }

    private static string Encode(SecretDeclaration declaration, string value) =>
        declaration.Encoding == SecretEncoding.Base64
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            : value;

    /// <summary>
    /// Runs the value command of a secret on the host and takes its output, without its
    /// surrounding white space, as the value. Never shows the output. Returns why there is no
    /// value when the command fails or prints nothing.
    /// </summary>
    private async Task<(SecretAnswer? Answer, string? Failure)> RunValueCommandAsync(
        SecretDeclaration declaration,
        string valueCommand,
        CancellationToken cancellationToken
    )
    {
        var result = await commands.RunAsync(valueCommand, cancellationToken);
        if (!result.Succeeded)
        {
            return (
                null,
                $"the host command '{valueCommand}' failed with exit code {result.ExitCode}."
            );
        }

        var value = result.Output.Trim();
        if (value.Length == 0)
        {
            return (null, $"the host command '{valueCommand}' printed nothing.");
        }

        console.WriteLine(
            $"Took the value of '{declaration.Service}' from the output of the host command: {valueCommand}"
        );
        return (
            new SecretAnswer(Encode(declaration, value), new Dictionary<string, string>()),
            null
        );
    }

    private string? ReadSecret(string prompt)
    {
        var value = console.ReadSecret(prompt);
        if (!string.IsNullOrEmpty(value))
        {
            console.WriteLine($"Received {value.Length} characters.");
        }

        return value;
    }

    /// <summary>
    /// Asks for the value of a service and stores it: a custom secret for a service sbx
    /// does not know. A secret with a command asks nothing; sbx runs the command. A custom
    /// secret is not stored over a conflicting one.
    /// </summary>
    /// <param name="declaration">The secret; a withheld command is never run.</param>
    /// <param name="domains">The domains the kits send the secret to; empty when no kit asks for it.</param>
    /// <param name="stored">The secrets the host keeps.</param>
    /// <param name="cancellationToken">Cancels the store calls.</param>
    public async Task<SecretSetting> SetAsync(
        SecretDeclaration declaration,
        IReadOnlyList<string> domains,
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

        if (declaration.ValueCommand is { } valueCommand)
        {
            return await SetFromValueCommandAsync(declaration, valueCommand, cancellationToken);
        }

        var answer = GitHubTokenSignIn.Handles(declaration.Service, domains)
            ? await gitHubTokens.ObtainAsync(domains, cancellationToken) is { Length: > 0 } token
                ? new SecretAnswer(token, new Dictionary<string, string>())
                : null
            : Ask(declaration);
        if (answer is null || answer.Value.Length == 0)
        {
            return SecretSetting.Skipped;
        }

        return await StoreAsync(declaration, answer, cancellationToken);
    }

    /// <summary>Stores the output of the value command; stores nothing when it fails or prints nothing.</summary>
    private async Task<SecretSetting> SetFromValueCommandAsync(
        SecretDeclaration declaration,
        string valueCommand,
        CancellationToken cancellationToken
    )
    {
        var (answer, failure) = await RunValueCommandAsync(
            declaration,
            valueCommand,
            cancellationToken
        );
        return failure is not null
            ? new SecretSetting(SecretSettingResult.Failed, failure)
            : await StoreAsync(declaration, answer!, cancellationToken);
    }

    /// <summary>Stores the value, and remembers its inputs once the store succeeded.</summary>
    private async Task<SecretSetting> StoreAsync(
        SecretDeclaration declaration,
        SecretAnswer answer,
        CancellationToken cancellationToken
    )
    {
        var outcome = declaration.IsCustom
            ? await store.SetCustomAsync(declaration, answer.Value, cancellationToken)
            : await store.SetAsync(declaration.Service, answer.Value, cancellationToken);
        if (outcome.Succeeded)
        {
            remembered.Save(declaration.Service, answer.Remember);
        }

        return SecretSetting.From(outcome);
    }
}

/// <summary>The value of a secret as the user gave it, and its inputs to remember.</summary>
internal sealed record SecretAnswer(string Value, IReadOnlyDictionary<string, string> Remember);

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
