using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Sandboxes;

namespace Abox.Features.Sandbox;

/// <summary>
/// <c>abox exec</c>: runs a command in a sandbox, starting the sandbox when it is stopped.
/// It is not a user command and stays out of the help.
/// </summary>
internal sealed class ExecCommand(CommandRunner runner)
    : SandboxCommand("exec", "Run a command in a sandbox.", runner, SandboxExecInput.None);
