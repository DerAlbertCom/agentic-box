using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Sandbox;

/// <summary>
/// <c>abox pipe</c>: runs a command in a sandbox with the standard input and output
/// attached, and prints nothing else. The Herdr app calls it to reach a sandbox.
/// It is not a user command and stays out of the help.
/// </summary>
public sealed class PipeCommand : Command
{
    public PipeCommand(ISandboxExec exec, TrailingArguments trailing, IConsole console)
        : base("pipe", "Run a command in a sandbox, with the standard streams attached.") =>
        SandboxCommand.Configure(this, exec, trailing, console, SandboxExecInput.Forwarded);
}
