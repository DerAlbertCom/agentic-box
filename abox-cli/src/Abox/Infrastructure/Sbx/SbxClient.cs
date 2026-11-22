using Abox.Features.Shared.Configuration;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// The calls abox makes to <c>sbx</c> that are not yet behind a use-case interface.
/// </summary>
public sealed class SbxClient : ISbxClient
{
    private readonly SbxInvoker _invoker;
    private readonly SbxDevelopmentCertificates _certificates;
    private readonly SbxKitSources _kitSources;

    public SbxClient(IProcessRunner runner, AboxPaths paths)
    {
        _invoker = new SbxInvoker(runner);
        _certificates = new SbxDevelopmentCertificates(_invoker);
        _kitSources = new SbxKitSources(_invoker, paths);
    }

    public Task ActivateDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    ) =>
        _certificates.ActivateDevelopmentCertificateAsync(
            sandbox,
            pfx,
            password,
            cancellationToken
        );

    public Task<string> GetDevelopmentCertificatePasswordAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => _certificates.GetDevelopmentCertificatePasswordAsync(sandbox, cancellationToken);

    public Task VerifyDevelopmentCertificateActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => _certificates.VerifyDevelopmentCertificateActivationAsync(sandbox, cancellationToken);

    public Task RefreshDevelopmentCertificateBootstrapAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => _certificates.RefreshDevelopmentCertificateBootstrapAsync(sandbox, cancellationToken);

    public Task EnsureKitSourceAllowedAsync(
        string repositoryPrefix,
        CancellationToken cancellationToken
    ) => _kitSources.EnsureKitSourceAllowedAsync(repositoryPrefix, cancellationToken);

    /// <summary>The sandboxes sbx knows about.</summary>
    public async Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(
        CancellationToken cancellationToken
    )
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
    /// reach <see cref="SbxCreateRequest.Progress"/> live, with known secrets redacted.
    /// </summary>
    public async Task<SbxOutcome> CreateAsync(
        SbxCreateRequest request,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "create",
            request.KitSource,
            request.Location,
            "--name",
            request.Name,
            "--pull",
            PullPolicyArgument(request.PullPolicy),
            "--no-input",
        };

        foreach (var mixin in request.Mixins)
        {
            arguments.Add("--kit");
            arguments.Add(mixin);
        }

        foreach (var (name, value) in request.KitArguments)
        {
            arguments.Add("--kit-arg");
            arguments.Add($"{name}={value}");
        }

        foreach (var mount in request.Mounts)
        {
            arguments.Add("--mount");
            arguments.Add(mount.ToString());
        }

        if (request.StaticMcpServers.Count > 0)
        {
            arguments.Add("--static-mcp");
            arguments.Add(string.Join(",", request.StaticMcpServers));
        }

        var process = new ProcessRequest(
            SbxInvoker.Executable,
            arguments,
            Environment: request.HostDockerContext is { } context
                ? new Dictionary<string, string>
                {
                    ["SBX_KIT_BUILDER"] = "host",
                    ["BUILDX_BUILDER"] = context,
                    ["DOCKER_CONTEXT"] = context,
                }
                : null
        );
        var redactor = new SecretRedactor(process);
        if (request.Progress is { } progress)
            process = process with
            {
                OutputLineSink = line => progress(redactor.Redact(line.Text)),
            };

        var outcome = await _invoker.RunOutcomeAsync(process, cancellationToken);
        return outcome with { Output = redactor.Redact(outcome.Output) };
    }

    private static string PullPolicyArgument(PullPolicy policy) =>
        policy switch
        {
            PullPolicy.Never => "never",
            PullPolicy.Always => "always",
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

    /// <summary>Removes a sandbox without asking.</summary>
    public Task<SbxOutcome> RemoveSandboxAsync(string name, CancellationToken cancellationToken)
    {
        return _invoker.RunOutcomeAsync(
            new ProcessRequest(SbxInvoker.Executable, ["rm", name, "--force"]),
            cancellationToken
        );
    }

    /// <summary>Whether a command exists inside a sandbox.</summary>
    public async Task<bool> HasProgramAsync(
        string sandbox,
        string program,
        CancellationToken cancellationToken
    )
    {
        var result = await _invoker.RunAsync(
            new ProcessRequest(
                SbxInvoker.Executable,
                ["exec", sandbox, "sh", "-c", "command -v \"$1\"", "_", program]
            ),
            cancellationToken
        );

        return result.Succeeded;
    }

    /// <summary>Where the runtime put the workspace of the sandbox.</summary>
    public async Task<string?> GetWorkspaceDirectoryAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var result = await _invoker.RunAsync(
            new ProcessRequest(
                SbxInvoker.Executable,
                ["exec", sandbox, "sh", "-c", "printf %s \"$WORKSPACE_DIR\""]
            ),
            cancellationToken
        );

        var directory = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    /// <summary>The MCP servers registered on the host.</summary>
    public async Task<IReadOnlyList<string>> ListMcpServersAsync(
        CancellationToken cancellationToken
    )
    {
        var servers = await _invoker.RunJsonAsync<List<SbxMcpServer>>(
            ["mcp", "ls", "--json"],
            "sbx mcp ls",
            cancellationToken
        );
        return servers
            .Select(server => server.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    /// <summary>Registers an MCP server on the host.</summary>
    public Task<SbxOutcome> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    )
    {
        return _invoker.RunOutcomeAsync(
            new ProcessRequest(SbxInvoker.Executable, ["mcp", "add", name, "--url", url]),
            cancellationToken
        );
    }
}
