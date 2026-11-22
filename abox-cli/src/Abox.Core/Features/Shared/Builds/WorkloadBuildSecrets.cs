namespace Abox.Features.Shared.Builds;

/// <summary>Build secrets that workload recipes may mount.</summary>
public static class WorkloadBuildSecrets
{
    /// <summary>The name under which a workload recipe reads the host's GitHub token while it is built.</summary>
    public const string GitHubToken = "github_token";
}
