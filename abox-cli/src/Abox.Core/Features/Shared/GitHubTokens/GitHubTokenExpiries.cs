using System.Text.Json;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.Shared.GitHubTokens;

/// <summary>
/// When the GitHub tokens abox stored expire, kept in abox's state, so <c>create</c> and
/// <c>run</c> can warn before a token stops working. The token itself is never kept here.
/// </summary>
internal sealed class GitHubTokenExpiries(AboxPaths paths, TimeProvider clock, IConsole console)
{
    /// <summary>How long before the expiry the warning starts.</summary>
    public static readonly TimeSpan WarningPeriod = TimeSpan.FromDays(7);

    private string FilePath => Path.Combine(paths.StateDirectory, "github-tokens.json");

    /// <summary>Remembers when the token of a service expires; null when it does not.</summary>
    public void Record(string service, string host, DateTimeOffset? expiresAt)
    {
        var tokens = Load().Where(token => token.Service != service).ToList();
        if (expiresAt is { } expiry)
        {
            tokens.Add(new GitHubTokenExpiry(service, host, expiry));
        }

        Save(tokens);
    }

    /// <summary>Forgets the token of a service, for example when its secret is removed.</summary>
    public void Forget(string service)
    {
        var tokens = Load();
        if (tokens.Any(token => token.Service == service))
        {
            Save(tokens.Where(token => token.Service != service).ToList());
        }
    }

    /// <summary>
    /// Warns about every token that has expired or expires within the warning period. An
    /// unreadable record is warned about too, never an error: the warning must not stop a command.
    /// </summary>
    public void WarnWhenClose()
    {
        List<GitHubTokenExpiry> tokens;
        try
        {
            tokens = Load();
        }
        catch (AboxException exception)
        {
            console.WriteError($"Warning: {exception.Message}");
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var token in tokens.Where(token => token.ExpiresAt - now < WarningPeriod))
        {
            var date = token.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd");
            var when = token.ExpiresAt <= now ? $"expired on {date}" : $"expires on {date}";
            console.WriteError(
                $"Warning: the token of the secret '{token.Service}' for {token.Host} {when}. "
                    + $"Create a new one and store it with 'abox secrets set --service {token.Service}'."
            );
        }
    }

    private List<GitHubTokenExpiry> Load()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<GitHubTokenExpiry>>(File.ReadAllText(FilePath))
                ?? [];
        }
        catch (JsonException exception)
        {
            throw new AboxException(
                $"The token expiries in '{FilePath}' are not readable: {exception.Message}",
                exception
            );
        }
    }

    private void Save(IReadOnlyList<GitHubTokenExpiry> tokens)
    {
        Directory.CreateDirectory(paths.StateDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(tokens));
    }

    private sealed record GitHubTokenExpiry(string Service, string Host, DateTimeOffset ExpiresAt);
}
