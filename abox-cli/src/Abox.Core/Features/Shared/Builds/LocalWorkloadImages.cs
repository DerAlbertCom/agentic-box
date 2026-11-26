using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.Builds;

/// <summary>
/// The prebuilt workload image in the host image store; at create, the sandbox runtime
/// only wraps it as a thin runtime kit.
/// </summary>
internal sealed class LocalWorkloadImages(ILocalImageStore store)
{
    /// <summary>The name of the generated workload kit descriptor, without the extension.</summary>
    public const string WorkloadKitName = "workload";

    /// <summary>Writes the thin runtime kit that wraps the workload image, replacing an earlier one.</summary>
    public static void WriteRuntimeKit(
        string workloadDirectory,
        string runtimeDirectory,
        string reference
    )
    {
        if (Directory.Exists(runtimeDirectory))
            Directory.Delete(runtimeDirectory, recursive: true);
        Directory.CreateDirectory(runtimeDirectory);
        File.Copy(
            Path.Combine(workloadDirectory, WorkloadKitName + ".yaml"),
            Path.Combine(runtimeDirectory, WorkloadKitName + ".yaml")
        );
        File.WriteAllText(
            Path.Combine(runtimeDirectory, WorkloadKitName + ".dockerfile"),
            $"FROM {reference}\n"
        );
    }

    /// <summary>Fails before sandbox replacement; never rebuilds or pulls a missing local image.</summary>
    public async Task EnsureAvailableAsync(
        LocalWorkloadImage image,
        CancellationToken cancellationToken
    )
    {
        RequireImageTools();
        try
        {
            if (BuildSources.DirectoryHash(image.RuntimeKitDirectory) != image.RuntimeKitHash)
            {
                throw new AboxException(
                    "The thin runtime kit changed. Run 'abox build' again; create will not use changed build recipes."
                );
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                "The thin runtime kit is missing or unreadable. Run 'abox build' again.",
                exception
            );
        }

        await store.RequireHostBuilderAsync(image.ImageStore, cancellationToken);
        if (
            await store.ImageIdAsync(image.ImageStore, image.Reference, cancellationToken)
            != image.ImageId
        )
        {
            throw new AboxException(
                $"The prebuilt workload image '{image.Reference}' was replaced. Run 'abox build' again."
            );
        }
    }

    private void RequireImageTools()
    {
        if (!store.IsInstalled)
            throw new AboxException(
                "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
            );
    }
}
