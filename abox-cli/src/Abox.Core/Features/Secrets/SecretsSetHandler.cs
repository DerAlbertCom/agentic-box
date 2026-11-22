using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

/// <summary>Runs <c>abox secrets set</c>: asks for the secrets of the sandbox and stores them on the host.</summary>
internal sealed class SecretsSetHandler(Preflight preflight, SecretsService secrets)
    : ICommandHandler<SecretsSetParameters>
{
    public Task<int> HandleAsync(
        SecretsSetParameters parameters,
        CancellationToken cancellationToken
    ) =>
        SecretsAction.RunAsync(
            preflight,
            parameters.Location,
            parameters.Services,
            secrets.SetAsync,
            cancellationToken
        );
}
