using System.Text.Json;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>Reads published images with Docker; Docker handles credentials, so failures never echo its output.</summary>
public sealed class DockerPublishedImageReader(IHostPrograms programs, IProcessRunner runner)
    : IPublishedImageReader
{
    private readonly DockerCommand _docker = new(runner);

    public bool IsInstalled => programs.Exists(DockerCommand.Program);

    public async Task<string?> ResolveDigestAsync(
        string reference,
        CancellationToken cancellationToken
    )
    {
        var result = await _docker.RunAsync(
            DockerCommand.ImageTools(
                null,
                ["inspect", "--format", "{{json .Manifest}}", reference]
            ),
            false,
            OperationFailure("buildx"),
            cancellationToken
        );
        using var identity = JsonDocument.Parse(result.StandardOutput);
        return identity.RootElement.GetProperty("digest").GetString();
    }

    public async Task<RegistryManifest> InspectAsync(
        string reference,
        CancellationToken cancellationToken
    )
    {
        var result = await _docker.RunAsync(
            DockerCommand.ImageTools(null, ["inspect", "--raw", reference]),
            false,
            OperationFailure("buildx"),
            cancellationToken
        );
        return DockerCommand.ParseManifest(result.StandardOutput);
    }

    public async Task SaveAsync(
        string reference,
        string platform,
        string archive,
        CancellationToken cancellationToken
    )
    {
        await _docker.RunAsync(
            ["pull", "--platform", platform, reference],
            false,
            OperationFailure("pull"),
            cancellationToken
        );
        await _docker.RunAsync(
            ["image", "save", "--output", archive, reference],
            false,
            OperationFailure("image"),
            cancellationToken
        );
    }

    private static DockerFailure OperationFailure(string command) =>
        new(
            exitCode =>
                $"Cannot read the published squad metadata (docker {command}, exit code {exitCode}). Check the published tag and Docker/Buildx, and run 'docker login {RegistryImageName.Registry}' with read access to the set and its bundled components. For an older artifact, ask a maintainer to republish it with current abox. The existing sandbox was not replaced.",
            IncludeOutput: false
        );
}
