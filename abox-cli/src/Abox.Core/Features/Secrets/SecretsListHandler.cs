using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Preflight;

namespace Abox.Features.Secrets;

/// <summary>Runs <c>abox secrets list</c>: lists the services of the sandbox and whether they are stored.</summary>
internal sealed class SecretsListHandler(Preflight preflight, SecretsService secrets)
    : ICommandHandler<SecretsListParameters>
{
    public Task<int> HandleAsync(
        SecretsListParameters parameters,
        CancellationToken cancellationToken
    ) =>
        SecretsAction.RunAsync(
            preflight,
            parameters.Location,
            [],
            secrets.ListAsync,
            cancellationToken
        );
}
