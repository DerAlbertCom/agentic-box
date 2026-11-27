using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.GitHubTokens;

/// <summary>
/// Asks for the fine-grained personal access token of the <c>abox-github-enterprise</c> service:
/// abox names the permissions and the prefilled link, the user creates the token and pastes
/// it, abox asks the host whether it accepts the token and remembers when it expires. abox
/// never creates a token and never opens a browser. The GitHub CLI is not needed.
/// The token's tenant is the domain the kit's credential request names without an
/// <c>api.</c>, <c>uploads.</c> or <c>copilot-api.</c> prefix.
/// </summary>
internal sealed class GitHubTokenSignIn(
    IGitHubTokenCheck check,
    GitHubTokenExpiries expiries,
    IConsole console
)
{
    /// <summary>The service whose value is a fine-grained GitHub token the user creates.</summary>
    public const string Service = "abox-github-enterprise";

    /// <summary>Where the user reads how to create the token by hand.</summary>
    public const string SetupDocument = "docs/github-enterprise-token.md";

    /// <summary>The prefix of a fine-grained personal access token.</summary>
    private const string FineGrainedPrefix = "github_pat_";

    /// <summary>
    /// The permissions the token gets, as the template link names them, with what each is
    /// for. Administration stays out, so the token cannot create or delete repositories.
    /// </summary>
    private static readonly (string Name, string Level, string Purpose)[] Permissions =
    [
        ("metadata", "read", "mandatory"),
        ("contents", "write", "clone, pull, push, branches, tags"),
        ("pull_requests", "write", "open and update pull requests"),
        ("issues", "write", "create and update issues"),
        ("actions", "write", "dispatch workflows, read runs and logs"),
        ("workflows", "write", "push changes under .github/workflows"),
        ("statuses", "read", "see CI results"),
        ("discussions", "write", "discussions"),
        ("organization_projects", "write", "projects of the organization"),
        ("copilot_requests", "write", "GitHub Copilot"),
    ];

    /// <summary>
    /// Whether the service's value is got through this sign-in: it is the GitHub token,
    /// and a kit names the domains it is sent to.
    /// </summary>
    public static bool Handles(string service, IReadOnlyList<string> domains) =>
        service == Service && domains.Count > 0;

    /// <summary>The prefixes of a tenant's API hosts.</summary>
    private static readonly string[] ApiHostPrefixes = ["api.", "uploads.", "copilot-api."];

    /// <summary>
    /// The tenant the token belongs to: the first domain that is not one of the tenant's
    /// API hosts, or the first domain when all are.
    /// </summary>
    public static string Tenant(IReadOnlyList<string> domains) =>
        domains.FirstOrDefault(domain =>
            !ApiHostPrefixes.Any(prefix => domain.StartsWith(prefix, StringComparison.Ordinal))
        ) ?? domains[0];

    /// <summary>The page that creates the token, prefilled with its name and permissions.</summary>
    public static Uri TokenPage(string host)
    {
        var query = string.Join(
            '&',
            [
                "name=" + Uri.EscapeDataString("abox sandbox agents"),
                "description=" + Uri.EscapeDataString("Used by the agents in abox sandboxes"),
                .. Permissions.Select(permission => $"{permission.Name}={permission.Level}"),
            ]
        );
        return new Uri($"https://{host}/settings/personal-access-tokens/new?{query}");
    }

    /// <summary>
    /// Asks for the token the user created. Returns null when none was given. Without
    /// a terminal it fails with what the user does instead.
    /// </summary>
    /// <param name="domains">The domains the kit sends the token to.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public async Task<string?> ObtainAsync(
        IReadOnlyList<string> domains,
        CancellationToken cancellationToken
    )
    {
        var host = Tenant(domains);
        if (!console.IsInteractive)
        {
            throw MissingWithoutTerminal(host);
        }

        ShowRequirements(host);

        while (true)
        {
            var token = console.ReadSecret("Paste the token (empty to skip): ")?.Trim();
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var result = await CheckAsync(host, token, cancellationToken);
            if (result.Accepted)
            {
                expiries.Record(Service, host, result.ExpiresAt);
                console.WriteLine(Accepted(host, result.ExpiresAt));
                return token;
            }

            console.WriteError($"{host} did not accept the token: {result.Reason}");
            if (!console.Confirm("Paste another token?"))
            {
                return null;
            }
        }
    }

    /// <summary>Why the secret cannot be set without a terminal, and what to do instead.</summary>
    private static AboxException MissingWithoutTerminal(string host) =>
        new(
            $"The secret '{Service}' for {host} is not stored, and its token can only be pasted on a terminal. "
                + $"Create the token as {SetupDocument} describes, then run 'abox secrets set --service {Service}' on a terminal."
        );

    private Task<GitHubTokenCheck> CheckAsync(
        string host,
        string token,
        CancellationToken cancellationToken
    ) =>
        token.StartsWith(FineGrainedPrefix, StringComparison.Ordinal)
            ? check.CheckAsync(host, token, cancellationToken)
            : Task.FromResult(
                GitHubTokenCheck.Reject(
                    $"it is not a fine-grained personal access token ({FineGrainedPrefix}...)."
                )
            );

    private void ShowRequirements(string host)
    {
        var page = TokenPage(host);
        console.WriteLine(
            $"The secret '{Service}' is a fine-grained personal access token of {host}. Create it with these permissions:"
        );
        foreach (var (name, level, purpose) in Permissions)
        {
            console.WriteLine($"  {name}: {level} ({purpose})");
        }

        console.WriteLine(
            "  administration: none, so the token cannot create or delete repositories"
        );
        console.WriteLine(
            "Choose your organization as the resource owner, with all of its repositories."
        );
        console.WriteLine($"This link prefills them: {page}");
        console.WriteLine($"How to create it is in {SetupDocument}.");
    }

    private static string Accepted(string host, DateTimeOffset? expiresAt) =>
        expiresAt is { } expiry
            ? $"{host} accepted the token. It expires on {expiry.UtcDateTime:yyyy-MM-dd}."
            : $"{host} accepted the token. It does not expire.";
}
