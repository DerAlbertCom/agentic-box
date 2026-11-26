using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>Builds and inspects component images with host Docker and Buildx.</summary>
internal sealed class DockerComponentImages(IHostPrograms programs, IProcessRunner runner)
    : IComponentImageBuilder,
        ILocalImageStore
{
    private static readonly DockerFailure OperationFailure = new(exitCode =>
        $"Local component image operation failed (Docker, exit {exitCode}). Check the Docker context and run 'abox build' again. No source-build fallback is performed."
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

    public async Task<string> BuildAsync(
        ComponentImageBuild build,
        CancellationToken cancellationToken
    )
    {
        Directory.CreateDirectory(build.WorkDirectory);
        var metadata = Path.Combine(build.WorkDirectory, Guid.NewGuid().ToString("N") + ".json");
        var arguments = new List<string>
        {
            "--context",
            build.ImageStore,
            "buildx",
            "build",
            "--builder",
            build.ImageStore,
            "--platform",
            "linux/amd64",
            "--file",
            build.Descriptor,
            "--tag",
            build.Reference,
            "--metadata-file",
            metadata,
            "--provenance=false",
            "--load",
        };
        if (build.WithGitHubToken)
            arguments.AddRange(DockerCommand.GitHubBuildSecret());
        arguments.Add(build.Directory);
        try
        {
            await _docker.RunAsync(arguments, false, OperationFailure, cancellationToken);
            return DockerCommand.BuiltDigest(metadata)
                ?? throw new AboxException(
                    $"Docker did not report the digest of '{build.Reference}'. Run 'abox build' again."
                );
        }
        finally
        {
            File.Delete(metadata);
        }
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

    public async Task<string?> FindImageIdAsync(
        string context,
        string reference,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(
                DockerCommand.Program,
                ["--context", context, "image", "inspect", "--format", "{{.Id}}", reference]
            ),
            cancellationToken
        );
        var id = result.StandardOutput.Trim();
        return result.Succeeded && ImageReferences.IsDigest(id) ? id : null;
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
