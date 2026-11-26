using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>A use case's failure diagnostic and whether Docker output is safe to include.</summary>
internal sealed record DockerFailure(Func<int, string> Message, bool IncludeOutput = true);

/// <summary>Runs <c>docker</c> with list arguments and maps its OCI output to domain models.</summary>
internal sealed class DockerCommand(IProcessRunner runner)
{
    public const string Program = "docker";

    public const string DescriptorAnnotation = "vnd.docker.sandbox.kit.descriptor";

    public async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        bool attachConsole,
        DockerFailure failure,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(Program, arguments, AttachConsole: attachConsole),
            cancellationToken
        );
        if (!result.Succeeded)
            throw new AboxException(
                failure.Message(result.ExitCode)
                    + (
                        failure.IncludeOutput
                            ? Environment.NewLine
                                + (result.StandardError + result.StandardOutput).Trim()
                            : ""
                    )
            );
        return result;
    }

    public static IReadOnlyList<string> ImageTools(string? builder, IReadOnlyList<string> arguments)
    {
        var result = new List<string> { "buildx", "imagetools" };
        if (builder is not null)
        {
            result.AddRange(["--builder", builder]);
        }
        result.AddRange(arguments);
        return result;
    }

    public static IReadOnlyList<string> GitHubBuildSecret() =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
            ? []
            : ["--secret", $"id={WorkloadBuildSecrets.GitHubToken},env=GITHUB_TOKEN"];

    /// <summary>The image digest a Buildx metadata file records, or null when it cannot be read.</summary>
    public static string? BuiltDigest(string metadata)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadata));
            var digest = document.RootElement.GetProperty("containerimage.digest").GetString();
            return ImageReferences.IsDigest(digest) ? digest : null;
        }
        catch (Exception exception)
            when (exception
                    is JsonException
                        or KeyNotFoundException
                        or InvalidOperationException
                        or IOException
            )
        {
            return null;
        }
    }

    public static RegistryManifest ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 2)
            throw new JsonException("Expected an OCI image manifest or index.");
        if (root.TryGetProperty("manifests", out var manifests))
            return new RegistryManifest(null, manifests.EnumerateArray().Select(Platform).ToList());
        return new RegistryManifest(KitDescriptor(root), null);
    }

    private static JsonElement? KitDescriptor(JsonElement manifest)
    {
        if (
            !manifest.TryGetProperty("annotations", out var annotations)
            || !annotations.TryGetProperty(DescriptorAnnotation, out var annotation)
        )
        {
            return null;
        }
        using var descriptor = JsonDocument.Parse(annotation.GetString() ?? "");
        return descriptor.RootElement.Clone();
    }

    private static PlatformManifest Platform(JsonElement manifest)
    {
        var hasPlatform = manifest.TryGetProperty("platform", out var platform);
        return new PlatformManifest(
            Text(manifest, "digest"),
            hasPlatform ? Text(platform, "os") : null,
            hasPlatform ? Text(platform, "architecture") : null,
            manifest.TryGetProperty("annotations", out var annotations)
                && Text(annotations, "vnd.docker.reference.type") == "attestation-manifest"
        );
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() : null;
}
