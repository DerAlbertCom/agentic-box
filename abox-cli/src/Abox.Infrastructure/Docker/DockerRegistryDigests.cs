using System.Text.Json;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>Resolves registry references to their current digest with <c>docker buildx imagetools</c>.</summary>
internal sealed class DockerRegistryDigests(IProcessRunner runner) : IRegistryDigests
{
    private readonly DockerCommand _docker = new(runner);

    public async Task<string> DigestAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await _docker.RunAsync(
            DockerCommand.ImageTools(
                null,
                ["inspect", "--format", "{{json .Manifest}}", reference]
            ),
            false,
            new DockerFailure(exitCode =>
                $"Cannot resolve '{reference}' in its registry (docker buildx, exit code {exitCode}). Check the reference and run 'docker login {RegistryImageName.Registry}' when it is in JFrog."
            ),
            cancellationToken
        );
        var invalid = $"Docker did not report a valid digest for '{reference}'.";
        string? digest;
        try
        {
            using var manifest = JsonDocument.Parse(result.StandardOutput);
            digest = manifest.RootElement.GetProperty("digest").GetString();
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AboxException(invalid, exception);
        }

        return ImageReferences.IsDigest(digest) ? digest! : throw new AboxException(invalid);
    }
}
