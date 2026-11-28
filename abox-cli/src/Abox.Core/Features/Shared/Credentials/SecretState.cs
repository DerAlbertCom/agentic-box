using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// Where one service of a squad stands on the host: whether its value is stored and its
/// API key approved for the domains the kits send it to.
/// </summary>
/// <param name="Service">The name of the service.</param>
/// <param name="Request">What the kits ask for; null when no kit asks for it.</param>
/// <param name="Declaration">How the squad declares the value; null for an undeclared service.</param>
/// <param name="Stored">Whether the host keeps a value.</param>
/// <param name="Approved">Whether the API key is approved for every domain, or needs no approval.</param>
/// <param name="Conflict">A stored custom secret the declaration would break.</param>
internal sealed record SecretState(
    string Service,
    CredentialRequest? Request,
    SecretDeclaration? Declaration,
    bool Stored,
    bool Approved,
    CustomSecret? Conflict
)
{
    /// <summary>
    /// Whether the sandbox needs it: a kit asks for it without being optional, or the squad
    /// declares it as a custom secret that no kit asks for, which a kit's setup reads.
    /// </summary>
    public bool Required =>
        Request is { Optional: false } || Request is null && Declaration is { IsCustom: true };

    /// <summary>Whether its API key needs an approval for the kits' domains.</summary>
    public bool ApprovalNeeded => Request is { Domains.Count: > 0 };

    /// <summary>Whether the guided setup has anything to do for it: a kit sends it, or the squad declares it.</summary>
    public bool Applies => ApprovalNeeded || Declaration is not null;

    /// <summary>Whether the value is stored and, where needed, approved.</summary>
    public bool Ready => Stored && Approved;

    /// <summary>What it is for: the description of the kit's capability, else of the declaration.</summary>
    public string? Description => Declaration?.Description ?? Request?.Description;

    /// <summary>The state as the list of <c>abox secrets update</c> shows it.</summary>
    public string Summary =>
        Conflict is not null ? "conflict"
        : !Stored ? (Required ? "not set" : "optional, not set")
        : !Approved ? "stored, not approved"
        : "stored";

    /// <summary>The kits that ask for it; a custom secret no kit asks for says so.</summary>
    public string Source =>
        Request is { Kits.Count: > 0 } ? string.Join(", ", Request.Kits) : "custom secret";
}
