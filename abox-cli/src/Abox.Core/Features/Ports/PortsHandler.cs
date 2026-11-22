using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Ports;

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
        var selectedOffset = FolderConfig.SelectPortOffset(folderConfiguration, parameters.Offset);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        var sandbox = await sandboxes.RequireAsync(
            location.FullPath,
            folderConfiguration,
            "ports never creates or replaces one",
            cancellationToken
        );
        if (parameters.RefreshHttpsBootstrap)
        {
            await https.RefreshBootstrapAsync(sandbox.Name, cancellationToken);
            return 0;
        }
        await ports.PublishAsync(
            location.FullPath,
            sandbox.Name,
            selectedOffset,
            cancellationToken
        );
        return 0;
    }
}
