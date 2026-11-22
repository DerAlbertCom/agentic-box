using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Ports;

/// <summary>
/// Runs <c>abox ports</c>: selects the port offset, runs preflight and publishes the ports
/// of the existing sandbox, or refreshes only its HTTPS bootstrap.
/// </summary>
internal sealed class PortsHandler(
    Preflight preflight,
    FolderSandbox sandboxes,
    AppPorts ports,
    SandboxHttps https
) : ICommandHandler<PortsParameters>
{
    public async Task<int> HandleAsync(
        PortsParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var location = parameters.Location;
        var folderConfiguration = await location.FolderConfigurationAsync(cancellationToken);
        if (parameters.RefreshHttpsBootstrap)
        {
            if (!await preflight.EnsureReadyAsync(cancellationToken))
                return 1;
            var refreshed = await RequireSandboxAsync(
                location,
                folderConfiguration,
                cancellationToken
            );
            await https.RefreshBootstrapAsync(refreshed, cancellationToken);
            return 0;
        }

        var selectedOffset = FolderConfig.SelectPortOffset(folderConfiguration, parameters.Offset);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        var sandbox = await RequireSandboxAsync(location, folderConfiguration, cancellationToken);
        await ports.PublishAsync(location.FullPath, sandbox, selectedOffset, cancellationToken);
        return 0;
    }

    private async Task<string> RequireSandboxAsync(
        SandboxLocation location,
        FolderConfig? folderConfiguration,
        CancellationToken cancellationToken
    ) =>
        (
            await sandboxes.RequireAsync(
                location.FullPath,
                folderConfiguration,
                "ports never creates or replaces one",
                cancellationToken
            )
        ).Name;
}
