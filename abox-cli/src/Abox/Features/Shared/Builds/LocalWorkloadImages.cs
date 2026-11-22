using Abox.Infrastructure;

namespace Abox.Features.Shared.Builds;

/// <summary>Prebuilds workload content in host Docker; sbx only wraps it as a v3 kit at create.</summary>
public sealed class LocalWorkloadImages(
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
        var context = await HostContextAsync(cancellationToken);
        var (reference, imageId) = await BuildImageAsync(
            workloadDirectory,
            context,
            cancellationToken
        );
        WriteRuntimeKit(workloadDirectory, runtimeDirectory, reference);
        console.WriteLine($"Prebuilt workload: {reference} ({imageId}).");
        return new LocalWorkloadImage
        {
            Reference = reference,
            ImageId = imageId,
            DockerContext = context,
            RuntimeKitDirectory = runtimeDirectory,
            RuntimeKitHash = BuildSources.DirectoryHash(runtimeDirectory),
        };
    }

    private async Task<string> HostContextAsync(CancellationToken cancellationToken)
    {
        RequireDocker();
        return await builder.HostContextAsync(cancellationToken);
    }

    private async Task<(string Reference, string ImageId)> BuildImageAsync(
        string workloadDirectory,
        string context,
        CancellationToken cancellationToken
    )
    {
        var sourceHash = BuildSources.DirectoryHash(workloadDirectory);
        var reference = "docker.io/library/abox-workload:" + Guid.NewGuid().ToString("N");
        console.WriteLine($"Building reusable workload image '{reference}'...");
        await builder.BuildAsync(
            new WorkloadImageBuild(
                workloadDirectory,
                Path.Combine(workloadDirectory, WorkloadKitName + ".yaml"),
                reference,
                context
            ),
            cancellationToken
        );
        var imageId = await store.ImageIdAsync(context, reference, cancellationToken);
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
        RequireDocker();
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

        await store.RequireHostBuilderAsync(image.DockerContext, cancellationToken);
        if (
            await store.ImageIdAsync(image.DockerContext, image.Reference, cancellationToken)
            != image.ImageId
        )
        {
            throw new AboxException(
                $"The prebuilt workload image '{image.Reference}' was replaced. Run 'abox build' again."
            );
        }
    }

    private void RequireDocker()
    {
        if (!store.IsInstalled)
            throw new AboxException(
                "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
            );
    }
}
