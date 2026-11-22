using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

internal sealed class SecretsSetHandler(Preflight preflight, SecretsService secrets)
    : ICommandHandler<SecretsSetParameters>
{
    public async Task<int> HandleAsync(
        SecretsSetParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var location = parameters.Location;
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        if (!await preflight.EnsureReadyAsync(cancellationToken))
            return 1;
        return await secrets.SetAsync(
            new SecretsRequest(location.FullPath, folderConfig, parameters.Services),
            cancellationToken
        );
    }
}
