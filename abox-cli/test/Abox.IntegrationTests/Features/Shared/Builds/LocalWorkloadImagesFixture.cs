using Abox.Features.Shared.Builds;
using Abox.Infrastructure;

namespace Abox.Tests;

internal sealed class LocalWorkloadImagesFixture : IDisposable
{
    internal TempDirectory Root { get; } = new();
    internal IProcessRunner Runner { get; } = LocalWorkloadFactory.Runner();

    /// <summary>A prebuilt workload image as a build records it, with its thin runtime kit in <paramref name="runtime"/>.</summary>
    internal Task<LocalWorkloadImage> BuildAsync(string runtime)
    {
        var source = Root.CreateDirectory("source");
        Root.File(
            "source/workload.yaml",
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
        var reference = $"rwest.jfrog.io/repo/components/workload:{runtime}";
        var runtimeDirectory = Path.Combine(Root.Path, runtime);
        LocalWorkloadImages.WriteRuntimeKit(source, runtimeDirectory, reference);
        return Task.FromResult(
            new LocalWorkloadImage
            {
                Reference = reference,
                ImageId = LocalWorkloadFactory.ImageId,
                ImageStore = "test-context",
                RuntimeKitDirectory = runtimeDirectory,
                RuntimeKitHash = BuildSources.DirectoryHash(runtimeDirectory),
            }
        );
    }

    public void Dispose() => Root.Dispose();
}
