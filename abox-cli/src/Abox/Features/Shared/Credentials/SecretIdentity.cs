using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>What names a stored secret.</summary>
public enum SecretIdentityKind
{
    /// <summary>A custom secret, by the first host it is sent to.</summary>
    Host,

    /// <summary>A custom secret without hosts, by the variable the agents read.</summary>
    EnvironmentVariable,

    /// <summary>A secret of a service the host store knows by name.</summary>
    Service,
}

/// <summary>
/// The name a declared secret is stored, found and removed under, so storing, listing and
/// removing agree on which stored secret is the declaration's.
/// </summary>
/// <param name="Kind">What kind of name it is.</param>
/// <param name="Value">The host, variable or service.</param>
public sealed record SecretIdentity(SecretIdentityKind Kind, string Value)
{
    /// <summary>The identity of a declared secret.</summary>
    public static SecretIdentity Of(SecretDeclaration secret) =>
        secret.Host is { } host ? new(SecretIdentityKind.Host, host)
        : secret.EnvironmentVariable is { } variable
            ? new(SecretIdentityKind.EnvironmentVariable, variable)
        : new(SecretIdentityKind.Service, secret.Service);
}
