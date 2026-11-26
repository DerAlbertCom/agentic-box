using System.Text.Json;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>Publishes images with <c>docker buildx</c>; Docker output is part of every failure.</summary>
internal sealed class DockerRegistryPublisher(IHostPrograms programs, IProcessRunner runner)
    : IRegistryPublisher
{
    private static readonly DockerFailure OperationFailure = new(exitCode =>
        $"Publishing failed (docker buildx, exit code {exitCode}). Check the Buildx builder and run 'docker login {RegistryImageName.Registry}' with a JFrog identity allowed to push to the workload's publishRepository."
    );

    private readonly DockerCommand _docker = new(runner);

    public bool IsInstalled => programs.Exists(DockerCommand.Program);

    public async Task RequireReadyAsync(CancellationToken cancellationToken) =>
        await _docker.RunAsync(["buildx", "version"], false, OperationFailure, cancellationToken);

    public async Task<string?> PushAsync(RegistryPush push, CancellationToken cancellationToken)
    {
        var metadata = Path.Combine(push.WorkDirectory, Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string> { "buildx", "build" };
        if (push.Builder is not null)
            arguments.AddRange(["--builder", push.Builder]);
        arguments.AddRange([
            "--file",
            push.Descriptor,
            "--tag",
            push.Reference,
            "--metadata-file",
            metadata,
            "--provenance=mode=min",
            "--push",
        ]);
        if (push.WithGitHubToken)
            arguments.AddRange(DockerCommand.GitHubBuildSecret());
        arguments.Add(push.Directory);
        await _docker.RunAsync(arguments, true, OperationFailure, cancellationToken);
        return DockerCommand.BuiltDigest(metadata);
    }

    public async Task PushImageAsync(
        string imageStore,
        string reference,
        CancellationToken cancellationToken
    ) =>
        await _docker.RunAsync(
            ["--context", imageStore, "push", reference],
            true,
            OperationFailure,
            cancellationToken
        );

    public async Task TagAsync(
        string candidate,
        string reference,
        string? builder,
        CancellationToken cancellationToken
    ) =>
        await _docker.RunAsync(
            DockerCommand.ImageTools(
                builder,
                ["create", "--prefer-index=false", "--tag", reference, candidate]
            ),
            true,
            OperationFailure,
            cancellationToken
        );

    public async Task<RegistryManifest> InspectAsync(
        string reference,
        string? builder,
        CancellationToken cancellationToken
    )
    {
        var result = await _docker.RunAsync(
            DockerCommand.ImageTools(builder, ["inspect", "--raw", reference]),
            false,
            OperationFailure,
            cancellationToken
        );
        return DockerCommand.ParseManifest(result.StandardOutput);
    }
}
