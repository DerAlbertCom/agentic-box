namespace Abox.Features.Shared.Credentials;

/// <summary>
/// The approvals that let the sandbox runtime send the API key of a service to the domains
/// a kit lists.
/// </summary>
public interface ICredentialApprovals
{
    /// <summary>Whether the API key of a service is approved for every domain.</summary>
    bool Covers(string service, IReadOnlyList<string> domains);

    /// <summary>Approves the API key of a service for the domains, keeping every other approval.</summary>
    void Approve(string service, IReadOnlyList<string> domains);
}
