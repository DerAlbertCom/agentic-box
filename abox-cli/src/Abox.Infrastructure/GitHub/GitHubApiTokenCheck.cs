using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Abox.Features.Shared.GitHubTokens;

namespace Abox.Infrastructure.GitHub;

/// <summary>
/// Checks a token with <c>GET https://api.&lt;host&gt;/user</c> of the GitHub REST API, so
/// the host needs no GitHub CLI. The token goes only into the request's Authorization
/// header and never into a result or a message.
/// </summary>
internal sealed class GitHubApiTokenCheck(HttpClient client) : IGitHubTokenCheck
{
    private const string ExpirationHeader = "github-authentication-token-expiration";

    public async Task<GitHubTokenCheck> CheckAsync(
        string host,
        string token,
        CancellationToken cancellationToken
    )
    {
        var api = host == "github.com" ? "api.github.com" : $"api.{host}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{api}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("abox", "1.0"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json")
        );

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => GitHubTokenCheck.Accept(Expiry(response)),
                HttpStatusCode.Unauthorized => GitHubTokenCheck.Reject(
                    "the token is wrong, expired or revoked (401)."
                ),
                var status => GitHubTokenCheck.Reject(
                    $"{api} answered {(int)status} {response.ReasonPhrase}.".Replace(" .", ".")
                ),
            };
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested
            )
        {
            // The handler's message may quote the request; never repeat it, it could hold the token.
            return GitHubTokenCheck.Reject($"{api} could not be reached.");
        }
    }

    /// <summary>
    /// The expiry GitHub sends with a token that expires, such as
    /// <c>2026-12-27 10:00:00 UTC</c> or <c>2026-12-27 10:00:00 -0700</c>.
    /// </summary>
    private static DateTimeOffset? Expiry(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(ExpirationHeader, out var values))
            return null;

        var value = values
            .FirstOrDefault()
            ?.Trim()
            .Replace(" UTC", " +0000", StringComparison.Ordinal);
        return
            DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd HH:mm:ss zzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var expiry
            )
            || DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out expiry
            )
            ? expiry.ToUniversalTime()
            : null;
    }
}
