using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;

namespace Abox.Features.UserSecrets;

/// <summary>
/// Runs <c>abox user-secrets</c>: prepares the selected stores, then copies them into the
/// existing sandbox of the folder.
/// </summary>
internal sealed class UserSecretsHandler(
    Preflight preflight,
    UserSecretsCopy copy,
    FolderSandbox sandboxes,
    IConsole console
) : ICommandHandler<UserSecretsParameters>
{
    public async Task<int> HandleAsync(
        UserSecretsParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var folderConfig = (await parameters.Location.FolderConfigurationAsync(cancellationToken))!;
        var prepared = await copy.PrepareAsync(folderConfig, parameters.Force, cancellationToken);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        var sandbox = (
            await sandboxes.RequireAsync(
                parameters.Location.FullPath,
                folderConfig,
                "user-secrets never creates or replaces one",
                cancellationToken
            )
        ).Name;
        UserSecretsReport report;
        try
        {
            report = await copy.CopyAsync(prepared, sandbox, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            console.WriteError(
                "User-secret copying was cancelled. Completed files may remain replaced; retry 'abox user-secrets' for this folder. No cross-store rollback is attempted."
            );
            throw;
        }
        if (report.SafetyOverride)
            console.WriteLine(
                "Warning: Safety override used (--force): selected original values matched the global deny list; copy completed."
            );
        UserSecretsReportWriter.WriteStores(report, console);
        if (report.Stores.Count == 0)
            console.WriteLine("No user-secret stores selected; nothing copied.");
        return 0;
    }
}
