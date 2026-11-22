using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

/// <summary>Runs <c>abox secrets remove</c>: removes the secrets of the sandbox from the host.</summary>
internal sealed class SecretsRemoveHandler(Preflight preflight, SecretsService secrets)
    : ICommandHandler<SecretsRemoveParameters>
{
    public Task<int> HandleAsync(
        SecretsRemoveParameters parameters,
        CancellationToken cancellationToken
    ) =>
        SecretsAction.RunAsync(
            preflight,
            parameters.Location,
            parameters.Services,
            secrets.RemoveAsync,
            cancellationToken
        );
}
