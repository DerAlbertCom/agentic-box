using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Sbx;

/// <summary>The sandbox lifecycle through <c>sbx ls</c>, <c>sbx create</c> and <c>sbx rm</c>.</summary>
public sealed class SbxSandboxLifecycle(IProcessRunner runner) : ISandboxLifecycle
{
    private readonly SbxInvoker _invoker = new(runner);

    public async Task<IReadOnlyList<SandboxInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var sandboxes = await _invoker.RunJsonAsync<List<SbxSandbox>>(
            ["ls", "--json"],
            "sbx ls",
            cancellationToken
        );
        return sandboxes
            .Where(sandbox => !string.IsNullOrEmpty(sandbox.Name))
            .Select(sandbox => new SandboxInfo(
                sandbox.Name!,
                sandbox.Status ?? string.Empty,
                sandbox.CreatedAt
            )
            {
                Workspaces = sandbox.Workspaces ?? [],
            })
            .ToList();
    }

    /// <summary>
    /// Creates a sandbox from a workload kit and its mixins. sbx never asks: abox reads
    /// nothing from the terminal while it runs, so a question would wait forever. Its lines
    /// reach <see cref="SandboxDefinition.Progress"/> live, with known secrets redacted.
    /// </summary>
    public async Task<SandboxCreation> CreateAsync(
        SandboxDefinition definition,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "create",
            definition.KitSource,
            definition.Location,
            "--name",
            definition.Name,
            "--pull",
            PullPolicyArgument(definition.PullPolicy),
            "--no-input",
        };

        foreach (var mixin in definition.Mixins)
        {
            arguments.Add("--kit");
            arguments.Add(mixin);
        }

        foreach (var (name, value) in definition.KitArguments)
        {
            arguments.Add("--kit-arg");
            arguments.Add($"{name}={value}");
        }

        foreach (var mount in definition.Mounts)
        {
            arguments.Add("--mount");
            arguments.Add($"{mount.HostPath}:{mount.SandboxPath}");
        }

        if (definition.StaticMcpServers.Count > 0)
        {
            arguments.Add("--static-mcp");
            arguments.Add(string.Join(",", definition.StaticMcpServers));
        }

        var process = new ProcessRequest(
            _invoker.ExecutableName,
            arguments,
            Environment: definition.HostDockerContext is { } context
                ? new Dictionary<string, string>
                {
                    ["SBX_KIT_BUILDER"] = "host",
                    ["BUILDX_BUILDER"] = context,
                    ["DOCKER_CONTEXT"] = context,
                }
                : null
        );
        var redactor = new SecretRedactor(process);
        if (definition.Progress is { } progress)
            process = process with
            {
                OutputLineSink = line => progress(redactor.Redact(line.Text)),
            };

        var outcome = await _invoker.RunOutcomeAsync(process, cancellationToken);
        outcome = outcome with { Output = redactor.Redact(outcome.Output) };
        return new SandboxCreation(
            outcome.Output,
            outcome.Succeeded ? null : SbxFailureHeadline.Describe(outcome)
        );
    }

    public async Task<SandboxRemoval> RemoveAsync(string name, CancellationToken cancellationToken)
    {
        var outcome = await _invoker.RunOutcomeAsync(
            new ProcessRequest(_invoker.ExecutableName, ["rm", name, "--force"]),
            cancellationToken
        );
        return new SandboxRemoval(outcome.Succeeded, outcome.Output);
    }

    private static string PullPolicyArgument(PullPolicy policy) =>
        policy switch
        {
            PullPolicy.Never => "never",
            PullPolicy.Always => "always",
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };
}
