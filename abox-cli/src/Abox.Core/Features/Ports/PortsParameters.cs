using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Ports;

/// <summary>
/// The parameters of <c>abox ports</c>. With <see cref="RefreshHttpsBootstrap"/> the location
/// carries no folder configuration and there is no offset.
/// </summary>
internal sealed record PortsParameters(
    SandboxLocation Location,
    int? Offset,
    bool RefreshHttpsBootstrap
);
