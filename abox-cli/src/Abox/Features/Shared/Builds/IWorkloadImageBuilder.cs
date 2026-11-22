namespace Abox.Features.Shared.Builds;

/// <summary>A workload image to build from its kit directory into the host context's image store.</summary>
public sealed record WorkloadImageBuild(
    string Directory,
    string Descriptor,
    string Reference,
    string Context
);

/// <summary>Builds workload images into the host's image store, which sandboxes create from.</summary>
public interface IWorkloadImageBuilder
{
    /// <summary>Checks that the current host context can build into its own image store, and returns it.</summary>
    Task<string> HostContextAsync(CancellationToken cancellationToken);

    /// <summary>Builds the image and loads it into the context's image store.</summary>
    Task BuildAsync(WorkloadImageBuild build, CancellationToken cancellationToken);
}

/// <summary>Inspects workload images in the host's image store.</summary>
public interface ILocalWorkloadImageStore
{
    /// <summary>Whether the image tooling is installed on the host.</summary>
    bool IsInstalled { get; }

    /// <summary>Fails unless the context's only builder shares the host image store.</summary>
    Task RequireHostBuilderAsync(string context, CancellationToken cancellationToken);

    /// <summary>The content ID of a local image, which changes whenever the image is replaced.</summary>
    Task<string> ImageIdAsync(
        string context,
        string reference,
        CancellationToken cancellationToken
    );
}

/// <summary>Build secrets that workload recipes may mount.</summary>
public static class WorkloadBuildSecrets
{
    /// <summary>The BuildKit secret used by workload recipes and image builds.</summary>
    public const string GitHubToken = "github_token";
}
