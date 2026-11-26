using System.Globalization;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Sbx;

/// <summary>The sandbox lifecycle through <c>sbx ls</c>, <c>sbx create</c> and <c>sbx rm</c>.</summary>
internal sealed class SbxSandboxLifecycle(SbxInvoker invoker) : ISandboxLifecycle
{
    public async Task<IReadOnlyList<SandboxInfo>> ListAsync(CancellationToken cancellationToken)
    {
        var sandboxes = await invoker.RunJsonAsync<List<SbxSandbox>>(
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
        var process = invoker.Call(CreateArguments(definition)) with
        {
            Environment = HostBuilderEnvironment(definition.HostImageStore),
        };
        var redactor = new SecretRedactor(process);
        if (definition.Progress is { } progress)
            process = process with
            {
                OutputLineSink = line => progress(redactor.Redact(line.Text)),
            };

        var outcome = await invoker.RunOutcomeAsync(process, cancellationToken);
        outcome = outcome with { Output = redactor.Redact(outcome.Output) };
        return new SandboxCreation(
            outcome.Output,
            outcome.Succeeded ? null : SbxInvoker.FailureHeadline(outcome)
        );
    }

    private static List<string> CreateArguments(SandboxDefinition definition)
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
        AddRepeated(arguments, "--kit", definition.Mixins);
        AddRepeated(
            arguments,
            "--kit-arg",
            definition.KitArguments.Select(argument => $"{argument.Key}={argument.Value}")
        );
        AddRepeated(
            arguments,
            "--mount",
            definition.Mounts.Select(mount => $"{mount.HostPath}:{mount.SandboxPath}")
        );
        if (definition.Memory is { } memory)
            arguments.AddRange(["--memory", memory.ToString(CultureInfo.InvariantCulture) + "g"]);
        if (definition.Cpus is { } cpus)
            arguments.AddRange(["--cpus", cpus.ToString(CultureInfo.InvariantCulture)]);
        if (definition.StaticMcpServers.Count > 0)
            arguments.AddRange(["--static-mcp", string.Join(",", definition.StaticMcpServers)]);
        return arguments;
    }

    private static void AddRepeated(
        List<string> arguments,
        string option,
        IEnumerable<string> values
    )
    {
        foreach (var value in values)
            arguments.AddRange([option, value]);
    }

    /// <summary>Lets sbx build the thin kits with the host builder of a local build's image store.</summary>
    private static Dictionary<string, string>? HostBuilderEnvironment(string? hostImageStore) =>
        hostImageStore is { } context
            ? new Dictionary<string, string>
            {
                ["SBX_KIT_BUILDER"] = "host",
                ["BUILDX_BUILDER"] = context,
                ["DOCKER_CONTEXT"] = context,
            }
            : null;

    public async Task<SandboxRemoval> RemoveAsync(string name, CancellationToken cancellationToken)
    {
        var outcome = await invoker.RunOutcomeAsync(
            invoker.Call(["rm", name, "--force"]),
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
