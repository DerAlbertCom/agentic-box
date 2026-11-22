using System.CommandLine;

namespace Abox.Features.Shared.CommandLine;

/// <summary>
/// A command that describes only its syntax. It binds a parse result to its parameters;
/// the command runner runs the handler for them, built only when the action runs.
/// </summary>
public abstract class AboxCommand<TParameters> : Command
    where TParameters : notnull
{
    protected AboxCommand(string name, string description, CommandRunner runner)
        : base(name, description)
    {
        SetAction(
            (parseResult, cancellationToken) =>
                runner.RunAsync(this, parseResult, cancellationToken)
        );
    }

    /// <summary>
    /// Maps the parse result to the parameters, with every default already applied. Checks
    /// that depend only on the options throw an <c>AboxException</c>.
    /// </summary>
    internal abstract Task<TParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    );
}
