using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Sandboxes;

/// <summary>What to create.</summary>
/// <param name="Team">The team whose squads the sandbox is created from.</param>
/// <param name="Squad">The squad the sandbox is created from, or null when the command asks for one.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Tag">The tag of the published kit set or local build.</param>
/// <param name="FolderConfig">The folder configuration snapshot loaded by the caller; null means absent.</param>
/// <param name="AssumeYes">Whether the replacement of an existing sandbox is confirmed already.</param>
/// <param name="Local">Whether to use local kits instead of pulling the published set.</param>
/// <param name="ExistingSandbox">The actual identity already selected for an accepted run replacement, if any.</param>
/// <param name="Memory">The memory in GB given for this create; saved in abox.user.yaml once the sandbox is created. Null uses the folder configuration, then the sbx default.</param>
/// <param name="Cpus">The CPUs given for this create; saved like <paramref name="Memory"/>.</param>
internal sealed record SandboxCreationRequest(
    SquadResolver.ResolvedTeam Team,
    string? Squad,
    string Location,
    string Tag,
    FolderConfig? FolderConfig,
    bool AssumeYes = false,
    bool Local = false,
    SandboxInfo? ExistingSandbox = null,
    int? Memory = null,
    int? Cpus = null
);

/// <summary>
/// Creates the sandbox of a folder from its squad's published set or local build.
/// </summary>
internal sealed class SandboxCreator(
    SandboxRecordStore records,
    AboxPaths paths,
    FolderSandbox sandboxes,
    ISandboxLifecycle lifecycle,
    IMcpServerRegistry mcpServers,
    IConsole console,
    LocalSquadSource local,
    RemoteSquadSource remote,
    UserSecretsCopy userSecrets,
    AppPorts appPorts,
    SquadCredentials credentials,
    IYamlParser parser
)
{
    /// <summary>Creates the sandbox and returns the exit code of the command.</summary>
    public async Task<int> CreateAsync(
        SandboxCreationRequest request,
        CancellationToken cancellationToken
    )
    {
        RegistryImageName.ValidateTag(request.Tag);

        var location = Path.GetFullPath(request.Location);
        if (!Directory.Exists(location))
        {
            throw new AboxException($"The folder '{location}' does not exist.");
        }

        var folderConfig = request.FolderConfig;
        request = request with
        {
            Squad = folderConfig?.SelectSquad(request.Squad) ?? request.Squad,
        };
        ISquadSource source = request.Local ? local : remote;
        var selection = source.Resolve(request);
        if (folderConfig is { UserSecrets.Count: > 0 })
            console.WriteLine("Reading the selected user secrets...");
        var preparedSecrets = await userSecrets.PrepareAsync(
            folderConfig,
            force: false,
            cancellationToken
        );
        console.WriteLine("Finding the application ports of the folder...");
        var preparedPorts = await AppPorts.PrepareAsync(
            location,
            FolderConfig.SelectPortOffset(folderConfig),
            cancellationToken
        );
        console.WriteLine(
            selection.Reference is { } reference
                ? $"Reading the published squad set '{reference}'..."
                : $"Loading the local build of '{selection.Identity.Squad}' with the tag '{selection.Identity.Tag}'..."
        );
        var prepared = await source.LoadAsync(selection, cancellationToken);
        var manifest = prepared.Metadata;
        var state =
            manifest
                .State?.Select(entry =>
                    StateEntry.FromStored(entry?.SandboxPath, entry?.RelativePath)
                )
                .ToList()
            ?? throw new AboxException(
                "Missing stored state declarations. Rebuild or republish the squad."
            );
        StateEntry.EnsureDisjoint(state);

        var mcpToRegister = await FindMcpServersToRegisterAsync(manifest, cancellationToken);
        if (request.ExistingSandbox is null)
            console.WriteLine("Looking for an existing sandbox of the folder...");
        var existing =
            request.ExistingSandbox ?? await sandboxes.FindAsync(location, cancellationToken);
        var name = existing?.Name ?? SandboxName.For(location);
        var replace = ConfirmReplacement(existing, location, request.AssumeYes);
        cancellationToken.ThrowIfCancellationRequested();
        console.WriteLine("Checking the credentials of the squad...");
        await credentials.PrepareAsync(
            request.Team.RootDirectory,
            request.Team.Name,
            selection.Identity.Squad,
            location,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();
        await RegisterMcpServersAsync(mcpToRegister, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        console.WriteLine("Preparing the state directories...");
        var mounts = CreateStateDirectories(name, state);
        cancellationToken.ThrowIfCancellationRequested();
        if (replace)
        {
            await RemoveExistingSandboxAsync(name, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        console.WriteLine($"Creating the sandbox '{name}'...");
        var streamed = false;
        var outcome = await lifecycle.CreateAsync(
            new SandboxDefinition(
                prepared.KitSource,
                location,
                name,
                prepared.Mixins,
                prepared.Arguments,
                mounts,
                manifest.Mcp.Select(server => server.Name).ToList(),
                PullPolicy: prepared.PullPolicy,
                HostImageStore: prepared.HostImageStore,
                Progress: line =>
                {
                    streamed = true;
                    console.WriteLine(line.Length > 0 ? "  " + line : line);
                },
                Memory: request.Memory ?? folderConfig?.Memory,
                Cpus: request.Cpus ?? folderConfig?.Cpus
            ),
            cancellationToken
        );

        if (outcome.Failure is { } failure)
        {
            var retry = CreateRecoveryHints.Render(
                request.Team.Name,
                selection.Identity.Squad,
                request.Tag,
                request.Local ? manifest : null,
                location
            );
            throw new AboxException(
                CreateFailure(name, failure, outcome.Output, replace, retry, streamed)
            );
        }

        WriteNotes(outcome);

        records.Save(
            name,
            new SandboxRecord
            {
                Team = request.Team.Name,
                // The build's spelling, whatever case --squad was given in.
                Squad = manifest.Squad,
                Tag = request.Tag,
                BuildCreatedAt = manifest.CreatedAt,
                CreatedAt = DateTimeOffset.Now,
                KitReference = prepared.KitReference,
            }
        );

        // Like the record, the folder settings describe a sandbox that exists now: a cancel
        // stops the steps after them, not this small local write.
        await SaveFolderSelectionAsync(
            request,
            manifest.Squad,
            folderConfig,
            location,
            CancellationToken.None
        );

        if (folderConfig is not null)
        {
            console.WriteLine("Copying the user secrets into the sandbox...");
            await CopyUserSecretsAsync(preparedSecrets, preparedPorts, name, cancellationToken);
        }

        console.WriteLine("Publishing the application ports...");
        await ConfigureApplicationPortsAsync(preparedPorts, name, cancellationToken);

        console.WriteLine($"Created sandbox '{name}' for '{location}'.");
        console.WriteLine($"Open it with: abox run --location {location}");
        return 0;
    }

    /// <summary>
    /// Saves in abox.user.yaml what the next create of the folder needs: the team and squad
    /// unless the folder configuration gives them already, and the memory and CPUs given for
    /// this create. The sandbox exists already, so a failure is reported and the create goes on.
    /// </summary>
    private async Task SaveFolderSelectionAsync(
        SandboxCreationRequest request,
        string squad,
        FolderConfig? folderConfig,
        string location,
        CancellationToken cancellationToken
    )
    {
        var selection = new FolderSelection(
            folderConfig?.Team == request.Team.Name ? null : request.Team.Name,
            folderConfig?.Squad == squad ? null : squad,
            request.Memory,
            request.Cpus
        );
        if (selection == new FolderSelection(null, null, null, null))
            return;
        console.WriteLine($"Saving the folder settings in {FolderConfig.UserFile}...");
        try
        {
            await FolderConfig.SaveAsync(location, selection, parser, cancellationToken);
        }
        catch (AboxException exception)
        {
            console.WriteError(
                $"The sandbox was created, but its folder settings were not saved: {exception.Message}"
            );
        }
    }

    private async Task CopyUserSecretsAsync(
        PreparedUserSecrets preparedSecrets,
        PreparedAppPorts preparedPorts,
        string name,
        CancellationToken cancellationToken
    )
    {
        var recovery =
            "The sandbox was created and its record retained, but required user-secret copying did not complete. "
            + "Completed files may remain replaced; no cross-store rollback is attempted. "
            + $"Fix the folder configuration or destination/runtime and retry without recreating: abox user-secrets --location \"{preparedPorts.Location}\". "
            + $"Application-port/HTTPS setup has not started; after copying succeeds run: abox ports --location \"{preparedPorts.Location}\" --offset {preparedPorts.Offset}";
        try
        {
            var report = await userSecrets.CopyAsync(preparedSecrets, name, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            UserSecretsReportWriter.WriteStores(report, console);
        }
        catch (OperationCanceledException)
        {
            console.WriteError(recovery);
            throw;
        }
        catch (AboxException exception)
        {
            throw new AboxException($"{exception.Message}{Environment.NewLine}{recovery}");
        }
    }

    private async Task ConfigureApplicationPortsAsync(
        PreparedAppPorts prepared,
        string name,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await appPorts.PublishAsync(prepared, name, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (exception is AboxException or OperationCanceledException)
        {
            console.WriteError(
                "The sandbox was created and its record retained, but required application-port/HTTPS setup did not complete. "
                    + "Completed user-secret copies, mappings, relays or certificate configuration may remain; no rollback is attempted. "
                    + $"Fix the reported problem and retry without recreating: abox ports --location \"{prepared.Location}\" --offset {prepared.Offset}"
            );
            throw;
        }
    }

    private bool ConfirmReplacement(SandboxInfo? existing, string location, bool assumeYes)
    {
        if (existing is null)
        {
            return false;
        }

        if (!assumeYes)
        {
            console.WriteLine($"The sandbox '{existing.Name}' already exists for '{location}'.");
            console.WriteLine("Replacing it loses what is inside it:");
            console.WriteLine("  - the container, with everything in it");
            console.WriteLine("  - the worktrees under ~/worktrees");
            console.WriteLine("The state directories on the host are kept.");
            console.WriteLine();

            if (!console.Confirm("Replace the sandbox?"))
            {
                throw new AboxException("The sandbox was not replaced.");
            }
        }

        return true;
    }

    private async Task RemoveExistingSandboxAsync(string name, CancellationToken cancellationToken)
    {
        console.WriteLine($"Removing the sandbox '{name}'...");
        var removal = await lifecycle.RemoveAsync(name, cancellationToken);
        if (!removal.Removed)
        {
            throw new AboxException(
                $"sbx could not remove the sandbox '{name}'." + Detail(removal.Output)
            );
        }
    }

    private IReadOnlyList<SandboxMount> CreateStateDirectories(
        string name,
        IReadOnlyList<StateEntry> state
    )
    {
        var mounts = new List<SandboxMount>();

        foreach (var entry in state)
        {
            var hostPath = Path.Combine(paths.StateDirectory, name, entry.RelativePath);
            try
            {
                Directory.CreateDirectory(hostPath);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new AboxException(
                    $"Cannot prepare the state directory '{hostPath}'. Check its permissions and remove any conflicting file. The existing sandbox was not replaced.",
                    exception
                );
            }
            mounts.Add(new SandboxMount(hostPath, entry.SandboxPath));
        }

        return mounts;
    }

    private async Task<IReadOnlyList<ManifestMcp>> FindMcpServersToRegisterAsync(
        BuildManifest manifest,
        CancellationToken cancellationToken
    )
    {
        if (manifest.Mcp.Count == 0)
        {
            return [];
        }

        console.WriteLine("Checking the MCP servers of the squad...");
        foreach (var server in manifest.Mcp)
            new McpDeclaration(server.Name, server.Url).Validate($"squad {manifest.Squad}");
        var registered = (await mcpServers.ListAsync(cancellationToken)).ToHashSet(
            StringComparer.Ordinal
        );
        var pending = new List<ManifestMcp>();

        foreach (var server in manifest.Mcp)
        {
            if (server.Url is null)
            {
                if (!registered.Contains(server.Name))
                {
                    throw new AboxException(
                        $"The MCP server '{server.Name}' is not registered on this host. "
                            + $"Register it with 'sbx mcp add {server.Name} --url <url>', or give it an url in the configuration."
                    );
                }
            }
            else if (!registered.Contains(server.Name))
            {
                pending.Add(server);
            }
        }

        return pending;
    }

    private async Task RegisterMcpServersAsync(
        IReadOnlyList<ManifestMcp> servers,
        CancellationToken cancellationToken
    )
    {
        foreach (var server in servers)
        {
            console.WriteLine($"Registering the MCP server '{server.Name}'...");
            var registration = await mcpServers.AddAsync(
                server.Name,
                server.Url!,
                cancellationToken
            );
            if (!registration.Registered)
            {
                throw new AboxException(
                    $"The MCP server '{server.Name}' could not be registered."
                        + Detail(registration.Output)
                );
            }
        }
    }

    private static string CreateFailure(
        string name,
        string failure,
        string output,
        bool replaced,
        string retry,
        bool streamed
    )
    {
        var lines = new List<string> { $"sbx could not create the sandbox '{name}'.", failure };
        if (replaced)
        {
            lines.Add(
                $"The old sandbox '{name}' was removed before the create; the state directories on the host are kept."
            );
            lines.Add($"Recreate it with: {retry}");
        }
        else
        {
            lines.Add($"Retry with: {retry}");
        }

        output = output.Trim();
        if (streamed)
            lines.Add("The full sbx output is shown above.");
        else if (output.Length > 0)
            lines.Add($"Full sbx output:{Environment.NewLine}{output}");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The notes of sbx, such as a credential it did not inject, gathered after its progress.</summary>
    private void WriteNotes(SandboxCreation outcome)
    {
        foreach (var line in outcome.Output.Split('\n'))
        {
            var note = line.Trim();
            if (note.StartsWith("Note:", StringComparison.Ordinal))
            {
                console.WriteLine(note);
            }
        }
    }

    private static string Detail(string output)
    {
        output = output.Trim();
        return output.Length > 0 ? $" {output}" : string.Empty;
    }
}
