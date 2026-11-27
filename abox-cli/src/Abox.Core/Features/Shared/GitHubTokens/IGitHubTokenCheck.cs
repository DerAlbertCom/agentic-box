namespace Abox.Features.Shared.GitHubTokens;

/// <summary>Asks a GitHub host whether it accepts a token, without the GitHub CLI.</summary>
public interface IGitHubTokenCheck
{
    /// <summary>Whether the host accepts the token, and until when.</summary>
    /// <param name="host">The GitHub host, such as <c>tenant.ghe.com</c>.</param>
    /// <param name="token">The token; it never appears in the result.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    Task<GitHubTokenCheck> CheckAsync(
        string host,
        string token,
        CancellationToken cancellationToken
    );
}

/// <summary>What the host said about a token.</summary>
/// <param name="Accepted">Whether the host accepts it.</param>
/// <param name="Reason">Why it was not accepted; empty when it was.</param>
/// <param name="ExpiresAt">When the token expires, when the host says so.</param>
public sealed record GitHubTokenCheck(bool Accepted, string Reason, DateTimeOffset? ExpiresAt)
{
    /// <summary>The host accepts the token.</summary>
    public static GitHubTokenCheck Accept(DateTimeOffset? expiresAt) =>
        new(true, string.Empty, expiresAt);

    /// <summary>The host did not accept the token.</summary>
    public static GitHubTokenCheck Reject(string reason) => new(false, reason, null);
}
