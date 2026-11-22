using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;

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

    /// <summary>
    /// Reads the staged kit descriptors and the bundled squad metadata in one exec, which
    /// starts a stopped sandbox. Each file is printed on its own line as <c>kit</c>, its
    /// base64-encoded folder name and base64-encoded content, or <c>squad</c> and the
    /// base64-encoded <c>squad.json</c>, so no name or content can break the framing.
    /// </summary>
    public async Task<SandboxSquadFiles> ReadSquadFilesAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        const string script = """
            for descriptor in /usr/share/sandbox/kit/*/kit.yaml; do
              [ -f "$descriptor" ] || continue
              kit=${descriptor%/kit.yaml}
              printf 'kit '
              printf %s "${kit##*/}" | base64 | tr -d '\n'
              printf ' '
              base64 < "$descriptor" | tr -d '\n'
              printf '\n'
            done
            if [ -f /usr/share/abox/squad.json ]; then
              printf 'squad '
              base64 < /usr/share/abox/squad.json | tr -d '\n'
              printf '\n'
            fi
            """;
        var result = await _invoker.RunOrThrowAsync(
            new ProcessRequest(
                SbxInvoker.Executable,
                ["exec", sandbox, "sh", "-c", script],
                StandardInput: string.Empty
            ),
            $"sbx exec {sandbox}",
            cancellationToken
        );

        var kits = new List<StagedKitDescriptor>();
        string? squadJson = null;
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split(' ');
            if (fields is ["kit", var name, var descriptor])
                kits.Add(
                    new StagedKitDescriptor(Decode(name, sandbox), Decode(descriptor, sandbox))
                );
            else if (fields is ["squad", var squad])
                squadJson = Decode(squad, sandbox);
        }

        return new SandboxSquadFiles(kits, squadJson);
    }

    private static string Decode(string base64, string sandbox)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException exception)
        {
            throw new AboxException(
                $"Cannot read the squad metadata of the sandbox '{sandbox}': the output of 'sbx exec' is garbled.",
                exception
            );
        }
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

    /// <summary>Reads kit declarations; this command does not build recipe-based kits.</summary>
    public Task<SbxOutcome> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? arguments = null
    )
    {
        var command = new List<string> { "kit", "inspect", kitDirectory };
        foreach (var (name, value) in arguments ?? new Dictionary<string, string>())
        {
            command.AddRange(["--kit-arg", $"{name}={value}"]);
        }
        return _invoker.RunOutcomeAsync(
            new ProcessRequest(SbxInvoker.Executable, command),
            cancellationToken
        );
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

    /// <summary>The secrets stored on the host.</summary>
    public async Task<HostSecrets> ListSecretsAsync(CancellationToken cancellationToken)
    {
        var list = await _invoker.RunJsonAsync<SbxSecrets>(
            ["secret", "ls", "--json"],
            "sbx secret ls",
            cancellationToken
        );
        return new HostSecrets(
            list.Secrets.Select(secret => secret.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList(),
            list.CustomSecrets.Select(secret => new CustomSecret(
                    secret.Targets.Where(target => !string.IsNullOrEmpty(target)).ToList(),
                    secret.EnvironmentVariable
                ))
                .ToList()
        );
    }

    /// <summary>
    /// Stores the secret of a service sbx knows. The value goes through the standard
    /// input, so it stays out of the process list.
    /// </summary>
    public Task<SbxOutcome> SetSecretAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    )
    {
        return _invoker.RunOutcomeAsync(
            new ProcessRequest(
                SbxInvoker.Executable,
                ["secret", "set", service],
                StandardInput: value,
                UseUtf8StandardInput: true
            ),
            cancellationToken
        );
    }

    /// <summary>
    /// Stores the secret of a service sbx does not know. The value goes through the
    /// standard input, so it stays out of the process list. Without a value, sbx takes
    /// it from the command of the declaration and checks that the command gives one.
    /// </summary>
    public Task<SbxOutcome> SetCustomSecretAsync(
        SecretDeclaration declaration,
        string? value,
        CancellationToken cancellationToken
    )
    {
        var arguments = SbxCustomSecretArguments.For(declaration);

        if (value is null)
        {
            arguments.Add("--command");
            arguments.Add(
                declaration.Command?.Line
                    ?? throw new ArgumentException(
                        $"The secret '{declaration.Service}' has neither a value nor a command.",
                        nameof(value)
                    )
            );
            return _invoker.RunOutcomeAsync(
                new ProcessRequest(SbxInvoker.Executable, arguments),
                cancellationToken
            );
        }

        arguments.Add("--value-stdin");

        return _invoker.RunOutcomeAsync(
            new ProcessRequest(
                SbxInvoker.Executable,
                arguments,
                StandardInput: value,
                UseUtf8StandardInput: true
            ),
            cancellationToken
        );
    }

    /// <summary>
    /// Removes a secret from the host store. A secret of a service sbx does not know is
    /// removed by its target host, or by its environment variable when it has no host.
    /// </summary>
    public Task<SbxOutcome> RemoveSecretAsync(
        SecretDeclaration declaration,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string> { "secret", "rm" };

        var (kind, value) = declaration.Identity;
        if (kind == SecretIdentityKind.Host)
        {
            arguments.Add("--host");
        }
        else if (kind == SecretIdentityKind.EnvironmentVariable)
        {
            arguments.Add("--env-name");
        }

        arguments.Add(value);
        arguments.Add("--force");

        return _invoker.RunOutcomeAsync(
            new ProcessRequest(SbxInvoker.Executable, arguments),
            cancellationToken
        );
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
