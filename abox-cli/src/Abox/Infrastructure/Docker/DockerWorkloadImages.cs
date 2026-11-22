using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>Builds and inspects workload images with host Docker and Buildx.</summary>
public sealed class DockerWorkloadImages(IHostPrograms programs, IProcessRunner runner)
    : IWorkloadImageBuilder,
        ILocalWorkloadImageStore
{
    private static readonly DockerFailure OperationFailure = new(exitCode =>
        $"Local workload image operation failed (Docker, exit {exitCode}). Check the Docker context and run 'abox build' again. No source-build fallback is performed."
    );

    private readonly DockerCommand _docker = new(runner);

    public bool IsInstalled => programs.Exists(DockerCommand.Program);

    public async Task<string> HostImageStoreAsync(CancellationToken cancellationToken)
    {
        await _docker.RunAsync(["buildx", "version"], false, OperationFailure, cancellationToken);
        var context = (
            await _docker.RunAsync(["context", "show"], false, OperationFailure, cancellationToken)
        ).StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(context))
        {
            throw new AboxException("Docker did not report its current context.");
        }

        // A context's docker-driver builder shares the image store used by the thin FROM.
        // A selected docker-container/remote builder cannot resolve host-only images.
        await RequireHostBuilderAsync(context, cancellationToken);
        return context;
    }

    public string NewImageReference() =>
        "docker.io/library/abox-workload:" + Guid.NewGuid().ToString("N");

    public async Task BuildAsync(WorkloadImageBuild build, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "--context",
            build.ImageStore,
            "buildx",
            "build",
            "--builder",
            build.ImageStore,
            "--file",
            build.Descriptor,
            "--tag",
            build.Reference,
            "--provenance=false",
            "--load",
        };
        arguments.AddRange(DockerCommand.GitHubBuildSecret());
        arguments.Add(build.Directory);
        await _docker.RunAsync(arguments, false, OperationFailure, cancellationToken);
    }

    public async Task RequireHostBuilderAsync(string context, CancellationToken cancellationToken)
    {
        var builders = (
            await _docker.RunAsync(
                ["buildx", "ls", "--format", "json"],
                false,
                OperationFailure,
                cancellationToken
            )
        ).StandardOutput.Trim();
        var drivers = new HashSet<string>();
        try
        {
            foreach (var line in builders.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                var builder = document.RootElement;
                if (builder.GetProperty("Name").GetString() == context)
                {
                    drivers.Add(builder.GetProperty("Driver").GetString() ?? "");
                }
            }
        }
        catch (Exception exception)
            when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AboxException(
                "Cannot read Docker Buildx builders. Local images require a docker-driver builder; run 'abox build' again after fixing Buildx.",
                exception
            );
        }
        if (drivers.Count != 1 || !drivers.Contains("docker"))
        {
            throw new AboxException(
                $"Local workload images require the docker-driver builder for context '{context}', not '{string.Join(", ", drivers)}'. Select a Docker Engine context and run 'abox build' again."
            );
        }
    }

    public async Task<string> ImageIdAsync(
        string context,
        string reference,
        CancellationToken cancellationToken
    )
    {
        var id = (
            await _docker.RunAsync(
                ["--context", context, "image", "inspect", "--format", "{{.Id}}", reference],
                false,
                OperationFailure,
                cancellationToken
            )
        ).StandardOutput.Trim();
        if (!ImageReferences.IsDigest(id))
        {
            throw new AboxException(
                $"Docker did not report a valid content ID for '{reference}'. Run 'abox build' again."
            );
        }
        return id;
    }
}
