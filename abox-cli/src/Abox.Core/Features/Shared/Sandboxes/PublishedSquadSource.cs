using System.Runtime.InteropServices;
using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;

namespace Abox.Features.Shared.Sandboxes;

/// <summary>A pinned squad set and its authoritative runtime metadata.</summary>
internal sealed record PublishedSquad(string Reference, BuildManifest Metadata);

/// <summary>Reads authoritative runtime metadata from a processed, digest-pinned squad set.</summary>
internal interface IPublishedSquadSource
{
    Task<PublishedSquad> LoadAsync(
        string reference,
        SquadBuildIdentity identity,
        CancellationToken cancellationToken
    );
}

/// <summary>Only registry reads: inspect the set, pull its small config mixin, and read its saved layers without executing or extracting them.</summary>
internal sealed class PublishedSquadSource(IPublishedImageReader registry, IConsole console)
    : IPublishedSquadSource
{
    public async Task<PublishedSquad> LoadAsync(
        string reference,
        SquadBuildIdentity identity,
        CancellationToken cancellationToken
    )
    {
        if (!registry.IsInstalled)
            throw new AboxException(
                $"Remote create needs Docker with Buildx to read the published squad metadata. Install Docker and run 'docker login {RegistryImageName.Registry}' with read access; no local build or team sources are needed."
            );

        using var temporary = new TemporaryDirectory("abox-pull-", "download", console);
        try
        {
            return await ReadPinnedAsync(reference, identity, temporary.Path, cancellationToken);
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or IOException
                        or InvalidDataException
                        or NotSupportedException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or FormatException
            )
        {
            throw new AboxException(
                $"The published set '{reference}' has missing, incompatible or invalid bundled squad metadata. Ask a maintainer to rebuild with 'abox build --team {identity.Team} --tag {identity.Tag}' and republish this tag with the current abox. No local metadata fallback is used; the existing sandbox was not replaced.",
                exception
            );
        }
    }

    private async Task<PublishedSquad> ReadPinnedAsync(
        string reference,
        SquadBuildIdentity identity,
        string directory,
        CancellationToken cancellationToken
    )
    {
        var architecture =
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        var (digest, manifest) = await ResolveManifestAsync(
            reference,
            architecture,
            cancellationToken
        );
        var pinned = ImageReferences.Pin(reference, digest);
        var descriptor = Descriptor(manifest, "workload");
        var config = ConfigurationReference(descriptor, identity);
        Descriptor(
            (await ResolveManifestAsync(config, architecture, cancellationToken)).Manifest,
            "mixin"
        );
        var archive = Path.Combine(directory, "config.tar");
        await registry.SaveAsync(config, "linux/" + architecture, archive, cancellationToken);
        var metadata = PublishedRuntimeMetadata.Read(
            PublishedConfigArchive.Read(archive),
            identity,
            descriptor
        );
        return new PublishedSquad(pinned, metadata);
    }

    private static string ConfigurationReference(
        JsonElement descriptor,
        SquadBuildIdentity identity
    )
    {
        var suffix = "/" + identity.Repository + "/components/config@";
        var configs = descriptor
            .GetProperty("kits")
            .EnumerateArray()
            .Select(kit => kit.GetProperty("ref").GetString() ?? "")
            .Where(value =>
                value.StartsWith(RegistryImageName.Registry + "/", StringComparison.Ordinal)
                && value.Contains(suffix, StringComparison.Ordinal)
            )
            .ToList();
        if (configs.Count != 1)
            throw new JsonException("Expected exactly one bundled squad configuration mixin.");
        var config = configs[0];
        var prefix = config[..config.IndexOf(suffix, StringComparison.Ordinal)];
        if (!RegistryImageName.IsValidRepository(prefix[(RegistryImageName.Registry.Length + 1)..]))
            throw new JsonException("Invalid configuration repository.");
        ImageReferences.Digest(
            config[(config.IndexOf(suffix, StringComparison.Ordinal) + suffix.Length)..]
        );
        return config;
    }

    private sealed record ResolvedManifest(string Digest, RegistryManifest Manifest);

    // Remote consumption selects only this host's Linux image. Publication deliberately verifies all platforms.
    private async Task<ResolvedManifest> ResolveManifestAsync(
        string reference,
        string architecture,
        CancellationToken cancellationToken,
        int depth = 0
    )
    {
        if (depth > 4)
            throw new JsonException("Too many nested image indexes.");

        var digest = ImageReferences.Digest(
            reference.Contains('@')
                ? reference[(reference.LastIndexOf('@') + 1)..]
                : await registry.ResolveDigestAsync(reference, cancellationToken)
        );
        var manifest = await registry.InspectAsync(
            ImageReferences.Repository(reference) + "@" + digest,
            cancellationToken
        );
        if (manifest.Platforms is not { } platforms)
            return new ResolvedManifest(digest, manifest);

        var selected = platforms
            .Where(platform => platform.Os == "linux" && platform.Architecture == architecture)
            .ToList();
        if (selected.Count != 1)
            throw new JsonException("No unambiguous runnable platform manifest.");
        var childDigest = ImageReferences.Digest(selected[0].Digest);
        return await ResolveManifestAsync(
            ImageReferences.Repository(reference) + "@" + childDigest,
            architecture,
            cancellationToken,
            depth + 1
        );
    }

    private static JsonElement Descriptor(RegistryManifest manifest, string kind) =>
        manifest.Descriptor(kind, "Expected a processed v3 kit descriptor.");
}
