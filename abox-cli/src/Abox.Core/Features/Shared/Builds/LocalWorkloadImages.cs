using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.Builds;

/// <summary>
/// Prebuilds workload content into the host image store; at create, the sandbox runtime
/// only wraps it as a thin runtime kit.
/// </summary>
internal sealed class LocalWorkloadImages(
    IWorkloadImageBuilder builder,
    ILocalWorkloadImageStore store,
    IConsole console
)
{
    /// <summary>The name of the generated workload kit descriptor, without the extension.</summary>
    public const string WorkloadKitName = "workload";

    public async Task<LocalWorkloadImage> BuildAsync(
        string workloadDirectory,
        string runtimeDirectory,
        CancellationToken cancellationToken
    )
    {
        var imageStore = await HostImageStoreAsync(cancellationToken);
        var (reference, imageId) = await BuildImageAsync(
            workloadDirectory,
            imageStore,
            cancellationToken
        );
        WriteRuntimeKit(workloadDirectory, runtimeDirectory, reference);
        console.WriteLine($"Prebuilt workload: {reference} ({imageId}).");
        return new LocalWorkloadImage
        {
            Reference = reference,
            ImageId = imageId,
            ImageStore = imageStore,
            RuntimeKitDirectory = runtimeDirectory,
            RuntimeKitHash = BuildSources.DirectoryHash(runtimeDirectory),
        };
    }

    private async Task<string> HostImageStoreAsync(CancellationToken cancellationToken)
    {
        RequireImageTools();
        return await builder.HostImageStoreAsync(cancellationToken);
    }

    private async Task<(string Reference, string ImageId)> BuildImageAsync(
        string workloadDirectory,
        string imageStore,
        CancellationToken cancellationToken
    )
    {
        var sourceHash = BuildSources.DirectoryHash(workloadDirectory);
        var reference = builder.NewImageReference();
        console.WriteLine($"Building reusable workload image '{reference}'...");
        await builder.BuildAsync(
            new WorkloadImageBuild(
                workloadDirectory,
                Path.Combine(workloadDirectory, WorkloadKitName + ".yaml"),
                reference,
                imageStore
            ),
            cancellationToken
        );
        var imageId = await store.ImageIdAsync(imageStore, reference, cancellationToken);
        if (BuildSources.DirectoryHash(workloadDirectory) != sourceHash)
        {
            throw new AboxException(
                "The workload sources changed during the image build. Run 'abox build' again."
            );
        }

        return (reference, imageId);
    }

    private static void WriteRuntimeKit(
        string workloadDirectory,
        string runtimeDirectory,
        string reference
    )
    {
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
