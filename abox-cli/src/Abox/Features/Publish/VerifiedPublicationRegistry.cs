using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Registry;
using Abox.Infrastructure;

namespace Abox.Features.Publish;

internal enum PublicationImageKind
{
    Workload,
    Mixin,
    Set,
}

internal sealed record PublicationPush(
    string Directory,
    string Reference,
    PublicationImageKind Kind,
    string PublicationDirectory
);

/// <summary>Publication's verified writes and all-runnable-platform inspection policy.</summary>
internal sealed class VerifiedPublicationRegistry(
    IRegistryPublisher registry,
    IConsole console,
    string? builder
)
{
    internal Task RequireBuildxAsync(CancellationToken cancellationToken) =>
        registry.RequireReadyAsync(cancellationToken);

    internal async Task ValidateRegistryKitsAsync(
        IEnumerable<ManifestKit> kits,
        CancellationToken cancellationToken
    )
    {
        foreach (var kit in kits.Where(kit => !kit.IsLocal))
        {
            var descriptors = await InspectAsync(
                new Inspection(kit.Reference, PublicationImageKind.Mixin),
                cancellationToken
            );
            foreach (var descriptor in descriptors)
                ValidateArguments(descriptor, kit.Arguments);
        }
    }

    internal async Task<string> PushVerifiedAsync(
        PublicationPush request,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine($"Publishing '{request.Reference}'...");
        var digest = await registry.PushAsync(
            new RegistryPush(
                request.Directory,
                PublicationSources.Descriptor(request.Directory),
                request.Reference,
                request.PublicationDirectory,
                builder,
                request.Kind == PublicationImageKind.Workload
            ),
            cancellationToken
        );
        if (!ImageReferences.IsDigest(digest))
            throw new AboxException(
                $"Cannot read the pushed digest for '{request.Reference}'; the requested set tag was not promoted."
            );

        var pinned = ImageReferences.Pin(request.Reference, digest!);
        await InspectAsync(new Inspection(pinned, request.Kind), cancellationToken);
        return pinned;
    }

    internal async Task PromoteAndConfirmAsync(
        string candidate,
        string requestedReference,
        CancellationToken cancellationToken
    )
    {
        // Only a previously verified immutable candidate reaches this stage. A failure after
        // promotion cannot roll back this tag, nor any earlier squad's successful publication.
        await registry.TagAsync(candidate, requestedReference, builder, cancellationToken);
        await InspectAsync(
            new Inspection(requestedReference, PublicationImageKind.Workload),
            cancellationToken
        );
    }

    private sealed record Inspection(string Reference, PublicationImageKind Kind, int Depth = 0);

    private async Task<IReadOnlyList<JsonElement>> InspectAsync(
        Inspection inspection,
        CancellationToken cancellationToken
    )
    {
        var (reference, _, depth) = inspection;
        // The frontend processes a set into a workload descriptor. Convert only
        // at the OCI descriptor boundary, retaining the image kind internally.
        var kind = inspection.Kind == PublicationImageKind.Mixin ? "mixin" : "workload";
        if (depth > 4)
            throw new AboxException(
                $"Registry artifact '{reference}' has too many nested image indexes."
            );
        try
        {
            var manifest = await registry.InspectAsync(reference, builder, cancellationToken);
            if (manifest.Platforms is not { } platforms)
                return [manifest.Descriptor(kind, $"Expected a v3 {kind} descriptor.")];
            return await InspectPlatformsAsync(platforms, inspection, cancellationToken);
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or FormatException
            )
        {
            throw new AboxException(
                $"Registry artifact '{reference}' did not retain a valid v3 {kind} descriptor. Publication stopped: {exception.Message}",
                exception
            );
        }
    }

    private async Task<IReadOnlyList<JsonElement>> InspectPlatformsAsync(
        IReadOnlyList<PlatformManifest> platforms,
        Inspection inspection,
        CancellationToken cancellationToken
    )
    {
        var leaves = new List<JsonElement>();
        foreach (var platform in platforms)
        {
            if (IsAttestation(platform))
                continue;
            var digest = platform.Digest;
            if (!ImageReferences.IsDigest(digest))
                throw new JsonException("Invalid platform manifest digest.");
            leaves.AddRange(
                await InspectAsync(
                    inspection with
                    {
                        Reference = ImageReferences.Repository(inspection.Reference) + "@" + digest,
                        Depth = inspection.Depth + 1,
                    },
                    cancellationToken
                )
            );
        }
        if (leaves.Count == 0)
            throw new JsonException("No runnable platform manifests.");
        return leaves;
    }

    private static bool IsAttestation(PlatformManifest platform) =>
        platform is { IsAttestation: true, Os: "unknown", Architecture: "unknown" };

    private static void ValidateArguments(
        JsonElement descriptor,
        IReadOnlyDictionary<string, string> arguments
    )
    {
        foreach (var name in arguments.Keys)
        {
            if (!HasCreatePhaseArgument(descriptor, name))
                throw new AboxException(
                    $"Registry kit argument '{name}' is undeclared or build-phase. Publishing supplied build-phase arguments is not supported."
                );
        }
    }

    private static bool HasCreatePhaseArgument(JsonElement descriptor, string name) =>
        descriptor.TryGetProperty("args", out var declarations)
        && declarations.TryGetProperty(name, out var declaration)
        && !IsBuildPhaseArgument(declaration);

    private static bool IsBuildPhaseArgument(JsonElement declaration) =>
        declaration.TryGetProperty("buildArg", out _)
        || declaration.TryGetProperty("buildSecret", out _);
}
