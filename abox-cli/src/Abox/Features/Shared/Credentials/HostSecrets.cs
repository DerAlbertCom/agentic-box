using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// The secrets the host keeps. A declaration is matched to them by the same identity that
/// removes it (<see cref="SecretIdentity"/>), so storing, listing and removing
/// agree on which stored secret is the declaration's.
/// </summary>
/// <param name="Services">The names of the services the host store knows by name.</param>
/// <param name="Custom">The custom secrets: the hosts they are sent to and the variable the agents read.</param>
public sealed record HostSecrets(IReadOnlyList<string> Services, IReadOnlyList<CustomSecret> Custom)
{
    /// <summary>Whether the host keeps the value of a secret.</summary>
    public bool Holds(SecretDeclaration secret) =>
        SecretIdentity.Of(secret).Kind == SecretIdentityKind.Service
            ? Services.Contains(secret.Service)
            : Custom.Any(custom => custom.Binds(secret));

    /// <summary>
    /// The stored custom secret that shares a host or the variable of a custom secret
    /// without being its binding, so storing the secret could break the other one.
    /// </summary>
    public CustomSecret? ConflictWith(SecretDeclaration secret) =>
        !secret.IsCustom || Holds(secret)
            ? null
            : Custom.FirstOrDefault(custom => custom.Overlaps(secret));
}

/// <summary>
/// A secret of a service the host store does not know by name.
/// </summary>
/// <param name="Targets">The hosts the secret is sent to.</param>
/// <param name="EnvironmentVariable">The variable the agents read.</param>
public sealed record CustomSecret(IReadOnlyList<string> Targets, string? EnvironmentVariable)
{
    /// <summary>
    /// Whether this is the binding of a custom secret: it is sent to all its hosts (or, for
    /// a secret without hosts, under its variable), and its variable, when both have one,
    /// is the same. A binding stored without a variable still counts.
    /// </summary>
    internal bool Binds(SecretDeclaration secret)
    {
        var named =
            SecretIdentity.Of(secret).Kind == SecretIdentityKind.Host
                ? secret.Hosts!.All(Targets.Contains)
                : EnvironmentVariable == secret.EnvironmentVariable;
        return named
            && (
                EnvironmentVariable is null
                || secret.EnvironmentVariable is null
                || EnvironmentVariable == secret.EnvironmentVariable
            );
    }

    internal bool Overlaps(SecretDeclaration secret) =>
        (secret.Hosts ?? []).Any(Targets.Contains)
        || secret.EnvironmentVariable is not null
            && EnvironmentVariable == secret.EnvironmentVariable;
}
