namespace Abox.Kits;

internal enum SecretIdentityKind
{
    Host,
    EnvironmentVariable,
    Service,
}

/// <summary>How a secret value is stored in the host store.</summary>
public enum SecretEncoding
{
    /// <summary>The value is stored as it is.</summary>
    Plain,

    /// <summary>The value is stored base64 encoded.</summary>
    Base64,
}

/// <summary>
/// One part of a secret that is built from several inputs.
/// </summary>
/// <param name="Name">The name of the part.</param>
/// <param name="Prompt">What the user is asked for.</param>
public sealed record SecretInput(string Name, string? Prompt);

/// <summary>
/// A secret of the host, as a configuration declares it.
/// </summary>
/// <param name="Service">The name of the secret in the host store. The kits name the same service.</param>
/// <param name="Prompt">What the user is asked for.</param>
/// <param name="Inputs">The parts the value is built from. Without them, one value is asked for.</param>
/// <param name="Separator">How the parts are joined.</param>
/// <param name="Encoding">How the value is stored.</param>
/// <param name="Hosts">The hosts of a service sbx does not know.</param>
/// <param name="EnvironmentVariable">The variable the agents read, for a service sbx does not know.</param>
/// <param name="Command">The host command whose output is the value; nobody is asked for it.</param>
/// <param name="Placeholder">The value the agents see instead of the secret, for a service sbx does not know.</param>
public sealed record SecretDeclaration(
    string Service,
    string? Prompt = null,
    IReadOnlyList<SecretInput>? Inputs = null,
    string Separator = ":",
    SecretEncoding Encoding = SecretEncoding.Plain,
    IReadOnlyList<string>? Hosts = null,
    string? EnvironmentVariable = null,
    string? Command = null,
    string? Placeholder = null
)
{
    /// <summary>The first host of a service sbx does not know; it names the secret.</summary>
    public string? Host => Hosts is { Count: > 0 } ? Hosts[0] : null;

    /// <summary>Whether the secret is one sbx does not know.</summary>
    public bool IsCustom => Host is not null || EnvironmentVariable is not null;

    /// <summary>Reads the name of an encoding; a missing name is plain.</summary>
    /// <param name="value">The name, such as <c>base64</c>.</param>
    /// <param name="source">Where the name comes from, for the message.</param>
    public static SecretEncoding ParseEncoding(string? value, string source) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "plain" => SecretEncoding.Plain,
            "base64" => SecretEncoding.Base64,
            _ => throw new AboxException(
                $"'{source}': '{value}' is not a known encoding. Known encodings: plain, base64."
            ),
        };

    internal (SecretIdentityKind Kind, string Value) Identity =>
        Host is not null ? (SecretIdentityKind.Host, Host)
        : EnvironmentVariable is not null
            ? (SecretIdentityKind.EnvironmentVariable, EnvironmentVariable)
        : (SecretIdentityKind.Service, Service);
}
