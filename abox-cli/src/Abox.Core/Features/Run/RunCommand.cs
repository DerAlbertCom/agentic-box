using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Launchers;

namespace Abox.Features.Run;

/// <summary>
/// <c>abox run</c>: starts a launcher for the sandbox of a folder.
/// </summary>
internal sealed class RunCommand : AboxCommand<RunParameters>
{
    private readonly Argument<string> _launcher = new("launcher")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "The launcher to start. Defaults to the shell.",
    };
    private readonly LocationOption _location = new();
    private readonly Option<bool?> _terminalTheme = new("--terminal-theme")
    {
        Arity = ArgumentArity.ExactlyOne,
        HelpName = "true|false",
        Description =
            $"Whether the sandbox sets the terminal colors from its theme. Overrides terminalTheme in the folder configuration ({FolderConfig.FileNames}) and in the user configuration; defaults to true.",
    };

    public RunCommand(CommandRunner runner, LauncherRegistry launchers)
        : base("run", "Start a launcher for the sandbox of a folder.", runner)
    {
        _launcher.CompletionSources.Add(launchers.All.Select(item => item.Id).ToArray());

        Arguments.Add(_launcher);
        Options.Add(_location);
        Options.Add(_terminalTheme);

        Description =
            "Start a launcher for the sandbox of a folder."
            + Environment.NewLine
            + Environment.NewLine
            + "Launchers:"
            + Environment.NewLine
            + string.Join(
                Environment.NewLine,
                launchers.All.Select(item => $"  {item.Id, -10} {item.Summary}")
            );
    }

    internal override async Task<RunParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    ) =>
        new(
            parseResult.GetValue(_launcher) ?? "shell",
            await defaults.SandboxLocationAsync(
                parseResult.GetValue(_location),
                FolderConfigurationLoading.OnFirstUse,
                cancellationToken
            ),
            defaults.TrailingArguments,
            parseResult.GetValue(_terminalTheme)
        );
}
