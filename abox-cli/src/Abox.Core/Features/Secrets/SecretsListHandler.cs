using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

internal sealed class SecretsListHandler(Preflight preflight, SecretsService secrets)
    : ICommandHandler<SecretsListParameters>
{
    public async Task<int> HandleAsync(
        SecretsListParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var location = parameters.Location;
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        return await secrets.ListAsync(
            new SecretsRequest(location.FullPath, folderConfig, []),
            cancellationToken
        );
    }
}
