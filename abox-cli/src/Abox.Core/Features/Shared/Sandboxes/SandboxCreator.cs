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
    /// <summary>What one create works with once the sandbox to create is known.</summary>
    private sealed record CreationPlan(
        SandboxCreationRequest Request,
        string Location,
        SquadSelection Selection,
        PreparedSquad Prepared,
        string Name,
        bool Replace
    )
    {
        public BuildManifest Manifest => Prepared.Metadata;
    }

    /// <summary>Creates the sandbox and returns the exit code of the command.</summary>
    public async Task<int> CreateAsync(
        SandboxCreationRequest request,
        CancellationToken cancellationToken
    )
    {
        RegistryImageName.ValidateTag(request.Tag);
        var location = ExistingFolder(request.Location);
        var folderConfig = request.FolderConfig;
        request = request with
        {
            Squad = folderConfig?.SelectSquad(request.Squad) ?? request.Squad,
        };
        ISquadSource source = request.Local ? local : remote;
        var selection = source.Resolve(request);
        var preparedSecrets = await PrepareUserSecretsAsync(folderConfig, cancellationToken);
        var preparedPorts = await PrepareApplicationPortsAsync(
            location,
            folderConfig,
            cancellationToken
        );
        var prepared = await LoadSquadAsync(source, selection, cancellationToken);
        var state = StoredState(prepared.Metadata);
        var mcpToRegister = await FindMcpServersToRegisterAsync(
            prepared.Metadata,
            cancellationToken
        );
        var existing = await FindExistingSandboxAsync(request, location, cancellationToken);
        var plan = new CreationPlan(
            request,
            location,
            selection,
            prepared,
            existing?.Name ?? SandboxName.For(location),
            ConfirmReplacement(existing, location, request.AssumeYes)
        );
        cancellationToken.ThrowIfCancellationRequested();
        var mounts = await PrepareHostAsync(plan, mcpToRegister, state, cancellationToken);
        await CreateSandboxAsync(plan, mounts, cancellationToken);
        SaveRecord(plan);

        // Like the record, the folder settings describe a sandbox that exists now: a cancel
        // stops the steps after them, not this small local write.
        await SaveFolderSelectionAsync(plan, CancellationToken.None);

        if (folderConfig is not null)
            await CopyUserSecretsAsync(
                preparedSecrets,
                preparedPorts,
                plan.Name,
                cancellationToken
            );

        console.WriteLine("Publishing the application ports...");
        await ConfigureApplicationPortsAsync(preparedPorts, plan.Name, cancellationToken);

        console.WriteLine($"Created sandbox '{plan.Name}' for '{location}'.");
        console.WriteLine($"Open it with: abox run --location {location}");
        return 0;
    }

    private static string ExistingFolder(string folder)
    {
        var location = Path.GetFullPath(folder);
        if (!Directory.Exists(location))
            throw new AboxException($"The folder '{location}' does not exist.");
        return location;
    }

    private async Task<PreparedUserSecrets> PrepareUserSecretsAsync(
        FolderConfig? folderConfig,
        CancellationToken cancellationToken
    )
    {
        if (folderConfig is { UserSecrets.Count: > 0 })
            console.WriteLine("Reading the selected user secrets...");
        return await userSecrets.PrepareAsync(folderConfig, force: false, cancellationToken);
    }

    private async Task<PreparedAppPorts> PrepareApplicationPortsAsync(
        string location,
        FolderConfig? folderConfig,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine("Finding the application ports of the folder...");
        return await AppPorts.PrepareAsync(
            location,
            FolderConfig.SelectPortOffset(folderConfig),
            cancellationToken
        );
    }

    private async Task<PreparedSquad> LoadSquadAsync(
        ISquadSource source,
        SquadSelection selection,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine(
            selection.Reference is { } reference
                ? $"Reading the published squad set '{reference}'..."
                : $"Loading the local build of '{selection.Identity.Squad}' with the tag '{selection.Identity.Tag}'..."
        );
        return await source.LoadAsync(selection, cancellationToken);
    }

    private static List<StateEntry> StoredState(BuildManifest manifest)
    {
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
        return state;
    }

    private async Task<SandboxInfo?> FindExistingSandboxAsync(
        SandboxCreationRequest request,
        string location,
        CancellationToken cancellationToken
    )
    {
        if (request.ExistingSandbox is { } existing)
            return existing;
        console.WriteLine("Looking for an existing sandbox of the folder...");
        return await sandboxes.FindAsync(location, cancellationToken);
    }

    /// <summary>Prepares what the sandbox needs on the host: credentials, MCP servers and state directories.</summary>
    private async Task<IReadOnlyList<SandboxMount>> PrepareHostAsync(
        CreationPlan plan,
        IReadOnlyList<ManifestMcp> mcpToRegister,
        IReadOnlyList<StateEntry> state,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine("Checking the credentials of the squad...");
        await credentials.PrepareAsync(
            plan.Request.Team.RootDirectory,
            plan.Request.Team.Name,
            plan.Selection.Identity.Squad,
            plan.Location,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();
        await RegisterMcpServersAsync(mcpToRegister, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        console.WriteLine("Preparing the state directories...");
        var mounts = CreateStateDirectories(plan.Name, state);
        cancellationToken.ThrowIfCancellationRequested();
        return mounts;
    }

    private async Task CreateSandboxAsync(
        CreationPlan plan,
        IReadOnlyList<SandboxMount> mounts,
        CancellationToken cancellationToken
    )
    {
        if (plan.Replace)
            await RemoveExistingSandboxAsync(plan.Name, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        console.WriteLine($"Creating the sandbox '{plan.Name}'...");
        var streamed = false;
        var outcome = await lifecycle.CreateAsync(
            Definition(
                plan,
                mounts,
                line =>
                {
                    streamed = true;
                    console.WriteLine(line.Length > 0 ? "  " + line : line);
                }
            ),
            cancellationToken
        );

        if (outcome.Failure is { } failure)
            throw new AboxException(CreateFailure(plan, failure, outcome.Output, streamed));

        WriteNotes(outcome);
    }

    private static SandboxDefinition Definition(
        CreationPlan plan,
        IReadOnlyList<SandboxMount> mounts,
        Action<string> progress
    )
    {
        var prepared = plan.Prepared;
        var folderConfig = plan.Request.FolderConfig;
        return new SandboxDefinition(
            prepared.KitSource,
            plan.Location,
            plan.Name,
            prepared.Mixins,
            prepared.Arguments,
            mounts,
            plan.Manifest.Mcp.Select(server => server.Name).ToList(),
            PullPolicy: prepared.PullPolicy,
            HostImageStore: prepared.HostImageStore,
            Progress: progress,
            Memory: plan.Request.Memory ?? folderConfig?.Memory,
            Cpus: plan.Request.Cpus ?? folderConfig?.Cpus
        );
    }

    private void SaveRecord(CreationPlan plan) =>
        records.Save(
            plan.Name,
            new SandboxRecord
            {
                Team = plan.Request.Team.Name,
                // The build's spelling, whatever case --squad was given in.
                Squad = plan.Manifest.Squad,
                Tag = plan.Request.Tag,
                BuildCreatedAt = plan.Manifest.CreatedAt,
                CreatedAt = DateTimeOffset.Now,
                KitReference = plan.Prepared.KitReference,
            }
        );

    /// <summary>
    /// Saves in abox.user.yaml what the next create of the folder needs: the team and squad
    /// unless the folder configuration gives them already, and the memory and CPUs given for
    /// this create. The sandbox exists already, so a failure is reported and the create goes on.
    /// </summary>
    private async Task SaveFolderSelectionAsync(
        CreationPlan plan,
        CancellationToken cancellationToken
    )
    {
        var request = plan.Request;
        var folderConfig = request.FolderConfig;
        var squad = plan.Manifest.Squad;
        var selection = new FolderSelection(
            folderConfig?.Team == request.Team.Name ? null : request.Team.Name,
            folderConfig?.Squad == squad ? null : squad,
            request.Memory,
            request.Cpus
        );
        if (selection.IsEmpty)
            return;
        console.WriteLine($"Saving the folder settings in {FolderConfig.UserFile}...");
        try
        {
            await FolderConfig.SaveAsync(plan.Location, selection, parser, cancellationToken);
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
        console.WriteLine("Copying the user secrets into the sandbox...");
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
        foreach (var server in manifest.Mcp.Where(server => server.Url is null))
        {
            if (!registered.Contains(server.Name))
                throw new AboxException(
                    $"The MCP server '{server.Name}' is not registered on this host. "
                        + $"Register it with 'sbx mcp add {server.Name} --url <url>', or give it an url in the configuration."
                );
        }

        var pending = manifest
            .Mcp.Where(server => server.Url is not null && !registered.Contains(server.Name))
            .ToList();
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
        CreationPlan plan,
        string failure,
        string output,
        bool streamed
    )
    {
        var request = plan.Request;
        var retry = CreateRecoveryHints.Render(
            request.Team.Name,
            plan.Selection.Identity.Squad,
            request.Tag,
            request.Local ? plan.Manifest : null,
            plan.Location
        );
        var lines = new List<string>
        {
            $"sbx could not create the sandbox '{plan.Name}'.",
            failure,
        };
        if (plan.Replace)
        {
            lines.Add(
                $"The old sandbox '{plan.Name}' was removed before the create; the state directories on the host are kept."
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
