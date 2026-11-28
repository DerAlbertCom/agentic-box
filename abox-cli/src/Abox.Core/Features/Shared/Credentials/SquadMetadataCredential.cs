namespace Abox.Features.Shared.Credentials;

/// <summary>
/// A credential the kits of a squad ask for, as <c>squad.json</c> records it at build time.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
/// <param name="Kits">The kits that ask for it.</param>
/// <param name="Domains">The domains its API key is sent to.</param>
/// <param name="Optional">Whether every kit that asks starts without it.</param>
/// <param name="Description">What the credential is for; missing in an older <c>squad.json</c>.</param>
internal sealed record SquadMetadataCredential(
    string Service,
    IReadOnlyList<string> Kits,
    IReadOnlyList<string> Domains,
    bool Optional,
    string? Description = null
)
{
    /// <summary>The request the record describes.</summary>
    public CredentialRequest ToRequest() => new(Service, Kits, Domains, Optional, Description);
}
