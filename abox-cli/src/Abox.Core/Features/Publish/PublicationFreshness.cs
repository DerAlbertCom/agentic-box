using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;

namespace Abox.Features.Publish;

/// <summary>
/// Refuses a build that no longer matches what it was built from: changed sources (also the
/// repository folder of a kit with a build step), a missing or replaced component image, or
/// a base image or registry kit whose tag now resolves to another digest.
/// </summary>
internal sealed class PublicationFreshness(IRegistryDigests digests, ILocalImageStore images)
{
    /// <summary>Checks the sources of the build, without Docker.</summary>
    public static void EnsureSourcesUnchanged(SelectedPublicationBuild selected)
    {
        var (identity, build) = selected;
        var rebuild = Rebuild(identity);

        try
        {
            BuildSources.EnsureUnchanged(build);
        }
        catch (AboxException exception)
        {
            throw new AboxException($"{exception.Message} {rebuild}", exception);
        }

        foreach (var kit in build.Kits.Where(kit => kit.IsLocal))
        {
            var component = kit.Component is { } name ? build.Component(name) : null;
            if (
                component is null
                || kit.Source is not { } source
                || !Directory.Exists(source)
                || BuildSources.InputHash(ComponentKind.Mixin, source, kit.Arguments)
                    != component.InputHash
            )
            {
                throw new AboxException(
                    $"The kit '{kit.Name}' of '{build.Squad}' changed since its build. {rebuild}"
                );
            }
        }
    }

    /// <summary>Checks the component images and the pinned registry digests of the build.</summary>
    public async Task EnsureImagesUnchangedAsync(
        SelectedPublicationBuild selected,
        CancellationToken cancellationToken
    )
    {
        var (identity, build) = selected;
        var rebuild = Rebuild(identity);
        foreach (var component in build.Components)
        {
            var imageId = await images.FindImageIdAsync(
                build.ImageStore!,
                component.Reference,
                cancellationToken
            );
            if (imageId != component.ImageId)
            {
                throw new AboxException(
                    $"The component image '{component.Reference}' is missing or was replaced. {rebuild}"
                );
            }
        }

        if (build.Base is { } baseImage && !baseImage.Reference.Contains('@'))
        {
            await EnsureSameDigestAsync(
                baseImage.Reference,
                baseImage.Digest,
                $"The base image '{baseImage.Reference}'",
                rebuild,
                cancellationToken
            );
        }

        foreach (var kit in build.Kits.Where(kit => !kit.IsLocal && !kit.Reference.Contains('@')))
        {
            await EnsureSameDigestAsync(
                kit.Reference,
                kit.Digest,
                $"The registry kit '{kit.Reference}'",
                rebuild,
                cancellationToken
            );
        }
    }

    private static string Rebuild(SquadBuildIdentity identity) =>
        $"Run 'abox build --team {identity.Team} --tag {identity.Tag}' before publishing.";

    private async Task EnsureSameDigestAsync(
        string reference,
        string? pinned,
        string subject,
        string rebuild,
        CancellationToken cancellationToken
    )
    {
        var current = await digests.DigestAsync(reference, cancellationToken);
        if (current != pinned)
        {
            throw new AboxException(
                $"{subject} now resolves to {current}, not to {pinned} as at build time. {rebuild}"
            );
        }
    }
}
