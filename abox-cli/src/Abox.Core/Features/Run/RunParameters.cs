using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Run;

/// <summary>
/// The parameters of <c>abox run</c>. The folder configuration of the location is loaded
/// only when the run flow first reads it.
/// </summary>
internal sealed record RunParameters(
    string LauncherId,
    SandboxLocation Location,
    IReadOnlyList<string> Argv,
    bool? TerminalTheme
);
