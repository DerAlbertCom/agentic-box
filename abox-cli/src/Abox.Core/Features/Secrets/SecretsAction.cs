using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

/// <summary>
/// The steps every <c>abox secrets</c> handler shares: read the folder configuration of the
/// location, run preflight, then act on the secrets request.
/// </summary>
internal static class SecretsAction
{
    public static async Task<int> RunAsync(
        Preflight preflight,
        SandboxLocation location,
        IReadOnlyList<string> services,
        Func<SecretsRequest, CancellationToken, Task<int>> act,
        CancellationToken cancellationToken
    )
    {
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        return await act(
            new SecretsRequest(location.FullPath, folderConfig, services),
            cancellationToken
        );
    }
}
