using Abox.Features.Shared.Configuration;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// The calls abox makes to <c>sbx</c> that are not yet behind a use-case interface.
/// </summary>
public sealed class SbxClient : ISbxClient
{
    public SbxClient(IProcessRunner runner, AboxPaths paths) { }
}
