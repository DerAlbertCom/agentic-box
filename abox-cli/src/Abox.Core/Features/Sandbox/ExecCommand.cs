using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Sandbox;

/// <summary>
/// <c>abox exec</c>: runs a command in a sandbox, starting the sandbox when it is stopped.
/// It is not a user command and stays out of the help.
/// </summary>
internal sealed class ExecCommand : Command
{
    public ExecCommand(ISandboxExec exec, TrailingArguments trailing, IConsole console)
        : base("exec", "Run a command in a sandbox.") =>
        SandboxCommand.Configure(this, exec, trailing, console);
}
