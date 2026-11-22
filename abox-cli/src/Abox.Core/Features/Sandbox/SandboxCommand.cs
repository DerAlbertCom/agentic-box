using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Sandbox;

/// <summary>The syntax of the hidden commands that exec into a named sandbox.</summary>
internal abstract class SandboxCommand : AboxCommand<SandboxExecParameters>
{
    private readonly Argument<string> _sandbox = new("sandbox")
    {
        Description = "The sandbox to run the command in.",
    };
    private readonly SandboxExecInput _input;

    protected SandboxCommand(
        string name,
        string description,
        CommandRunner runner,
        SandboxExecInput input
    )
        : base(name, description, runner)
    {
        _input = input;
        Hidden = true;
        Arguments.Add(_sandbox);
    }

    internal override Task<SandboxExecParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    )
    {
        var command = defaults.TrailingArguments;
        if (command.Count == 0)
            throw new AboxException($"no command given. Use: abox {Name} <sandbox> -- <command>");
        return Task.FromResult(
            new SandboxExecParameters(parseResult.GetValue(_sandbox)!, command, _input)
        );
    }
}
