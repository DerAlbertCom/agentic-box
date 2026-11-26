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
        try
        {
            using var manifest = JsonDocument.Parse(result.StandardOutput);
            var digest = manifest.RootElement.GetProperty("digest").GetString();
            if (ImageReferences.IsDigest(digest))
                return digest!;
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        { }

        throw new AboxException($"Docker did not report a valid digest for '{reference}'.");
    }
}
