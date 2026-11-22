using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.Ports;

/// <summary>
/// <c>abox ports</c>: publishes the discovered application ports of a folder's sandbox on
/// host loopback, or refreshes only its HTTPS bootstrap.
/// </summary>
internal sealed class PortsCommand : AboxCommand<PortsParameters>
{
    private readonly LocationOption _location = new();
    private readonly Option<int?> _offset = new("--offset")
    {
        Description =
            $"One-run port offset (-65534..65534). Overrides the folder portOffset ({FolderConfig.FileNames}), then default 10000.",
    };
    private readonly Option<bool> _refreshBootstrap = new("--refresh-https-bootstrap")
    {
        Description =
            "Refresh only the verified HTTPS reader after a CLI upgrade; no discovery, host export, root trust or port changes.",
    };

    public PortsCommand(CommandRunner runner)
        : base(
            "ports",
            "Publish discovered application ports on host loopback with an offset.",
            runner
        )
    {
        Options.Add(_location);
        Options.Add(_offset);
        Options.Add(_refreshBootstrap);
    }

    internal override async Task<PortsParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    )
    {
        var offset = parseResult.GetValue(_offset);
        var refresh = parseResult.GetValue(_refreshBootstrap);
        if (refresh && offset is not null)
            throw new AboxException(
                "--refresh-https-bootstrap cannot be combined with --offset; it changes no ports."
            );
        var location = await defaults.SandboxLocationAsync(
            parseResult.GetValue(_location),
            refresh ? FolderConfigurationLoading.Never : FolderConfigurationLoading.Now,
            cancellationToken
        );
        return new PortsParameters(location, offset, refresh);
    }
}
