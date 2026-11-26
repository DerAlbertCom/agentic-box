using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;

namespace Abox.Features.Build;

/// <summary>A component image to make: what it is built from and the hash of those inputs.</summary>
/// <param name="Name">The component's name under the squad's <c>components</c> repository.</param>
/// <param name="Kind">The kind its descriptor declares.</param>
/// <param name="Directory">The kit directory the image is built from.</param>
/// <param name="Descriptor">The kit descriptor in <paramref name="Directory"/>.</param>
/// <param name="Arguments">The kit arguments, part of the input hash.</param>
/// <param name="WithGitHubToken">Whether the recipe may mount the host's GitHub token.</param>
/// <param name="Inputs">The directory the input hash covers; null means <paramref name="Directory"/>. A kit with a build step is built from its staged copy, but its inputs are its folder in the repository.</param>
internal sealed record ComponentSource(
    string Name,
    string Kind,
    string Directory,
    string Descriptor,
    IReadOnlyDictionary<string, string> Arguments,
    bool WithGitHubToken = false,
    string? Inputs = null
)
{
    /// <summary>The hash of the inputs, which is also the image's tag.</summary>
    public string InputHash { get; } = BuildSources.InputHash(Kind, Inputs ?? Directory, Arguments);

    /// <summary>The input hash as the inputs are now.</summary>
    public string CurrentInputHash() =>
        BuildSources.InputHash(Kind, Inputs ?? Directory, Arguments);
}

/// <summary>Where the component images of one squad go, and what its previous build holds.</summary>
/// <param name="ImageStore">The host image store the images are built into.</param>
/// <param name="Repository">The squad's registry repository, such as <c>rwest.jfrog.io/repo/agentic-box-rwest-team/squad</c>.</param>
/// <param name="WorkDirectory">A directory for the builder's scratch files.</param>
/// <param name="Previous">The components of the previous build, reused when unchanged.</param>
/// <param name="Rebuild">Whether every component is built again, changed or not.</param>
internal sealed record ComponentTarget(
    string ImageStore,
    string Repository,
    string WorkDirectory,
    IReadOnlyList<BuildComponent> Previous,
    bool Rebuild
)
{
    /// <summary>The reference of a component image: its final registry name, tagged with its input hash.</summary>
    public string Reference(string name, string inputHash) =>
        $"{Repository}/components/{name}:{inputHash}";
}

/// <summary>
/// Makes the component images of a squad in the host image store: reuses a component of the
/// previous build when its inputs are unchanged and the store still holds its image, and
/// builds it otherwise.
/// </summary>
internal sealed class ComponentImages(
    IComponentImageBuilder builder,
    ILocalImageStore store,
    IConsole console
)
{
    /// <summary>Checks that the host can build component images and returns its image store.</summary>
    public async Task<string> ImageStoreAsync(CancellationToken cancellationToken)
    {
        if (!store.IsInstalled)
            throw new AboxException(
                "Building component images needs Docker with Buildx on PATH. Install Docker and run 'abox build' again."
            );
        return await builder.HostImageStoreAsync(cancellationToken);
    }

    /// <summary>
    /// The previous build's component when its name and input hash are unchanged and the
    /// image store still holds the same image; null when it has to be built.
    /// </summary>
    public async Task<BuildComponent?> ReusableAsync(
        ComponentTarget target,
        string name,
        string inputHash,
        CancellationToken cancellationToken
    )
    {
        if (target.Rebuild)
            return null;

        var reference = target.Reference(name, inputHash);
        var previous = target.Previous.FirstOrDefault(component =>
            component.Name == name
            && component.InputHash == inputHash
            && component.Reference == reference
        );
        if (previous is null)
            return null;

        var imageId = await store.FindImageIdAsync(target.ImageStore, reference, cancellationToken);
        if (imageId != previous.ImageId)
            return null;

        console.WriteLine($"Component '{name}' is up to date.");
        return previous;
    }

    /// <summary>Builds the component image and loads it into the image store.</summary>
    public async Task<BuildComponent> BuildAsync(
        ComponentTarget target,
        ComponentSource source,
        CancellationToken cancellationToken
    )
    {
        var reference = target.Reference(source.Name, source.InputHash);
        var descriptor = File.ReadAllText(source.Descriptor);
        console.WriteLine($"Building component '{source.Name}'...");
        var digest = await builder.BuildAsync(
            new ComponentImageBuild(
                source.Directory,
                source.Descriptor,
                reference,
                target.ImageStore,
                target.WorkDirectory,
                source.WithGitHubToken
            ),
            cancellationToken
        );
        var imageId = await store.ImageIdAsync(target.ImageStore, reference, cancellationToken);
        if (source.CurrentInputHash() != source.InputHash)
        {
            throw new AboxException(
                $"The sources of component '{source.Name}' changed during its image build. Run 'abox build' again."
            );
        }

        console.WriteLine($"Built component '{source.Name}': {reference} ({digest}).");
        return new BuildComponent
        {
            Name = source.Name,
            Kind = source.Kind,
            Reference = reference,
            Digest = digest,
            ImageId = imageId,
            InputHash = source.InputHash,
            Descriptor = descriptor,
            DescriptorFile = Path.GetFileName(source.Descriptor),
            Directory = source.Directory,
        };
    }
}
