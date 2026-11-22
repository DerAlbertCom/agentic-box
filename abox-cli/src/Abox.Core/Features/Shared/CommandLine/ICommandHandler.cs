namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// The work of one command: its parameters in, its exit code out. The parameters type
/// picks the handler.
/// </summary>
internal interface ICommandHandler<in TParameters>
    where TParameters : notnull
{
    Task<int> HandleAsync(TParameters parameters, CancellationToken cancellationToken);
}
