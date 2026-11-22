using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

internal sealed class SecretsRemoveHandler(Preflight preflight, SecretsService secrets)
    : ICommandHandler<SecretsRemoveParameters>
{
    public async Task<int> HandleAsync(
        SecretsRemoveParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var location = parameters.Location;
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        return await secrets.RemoveAsync(
            new SecretsRequest(location.FullPath, folderConfig, parameters.Services),
            cancellationToken
        );
    }
}
