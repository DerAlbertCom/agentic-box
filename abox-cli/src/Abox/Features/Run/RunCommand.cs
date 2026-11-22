using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Launchers;
using Abox.Features.Shared.Preflight;
using Abox.Infrastructure;

namespace Abox.Features.Run;

/// <summary>
/// <c>abox run</c>: starts a launcher for the sandbox of a folder.
/// </summary>
public sealed class RunCommand : Command
{
    public RunCommand(
        Lazy<AboxConfig> config,
        LauncherRegistry launchers,
        Preflight preflight,
        Lazy<RunFlow> run,
        TrailingArguments trailing,
        IConsole console
    )
        : base("run", "Start a launcher for the sandbox of a folder.")
    {
        var launcher = new Argument<string>("launcher")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "The launcher to start. Defaults to the shell.",
        };

        launcher.CompletionSources.Add(launchers.All.Select(item => item.Id).ToArray());

        var location = new Option<string>("--location")
        {
            Description =
                "The folder whose sandbox is opened. Defaults to the location of the user configuration, or the current directory.",
        };

        var terminalTheme = new Option<bool?>("--terminal-theme")
        {
            Arity = ArgumentArity.ExactlyOne,
            HelpName = "true|false",
            Description =
                $"Whether the sandbox sets the terminal colors from its theme. Overrides terminalTheme in the folder configuration ({FolderConfig.FileNames}) and in the user configuration; defaults to true.",
        };

        Arguments.Add(launcher);
        Options.Add(location);
        Options.Add(terminalTheme);

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

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        var request = new RunRequest(
                            parseResult.GetValue(launcher) ?? "shell",
                            config.Value.ResolveLocation(parseResult.GetValue(location)),
                            trailing.Values,
                            parseResult.GetValue(terminalTheme)
                        );

                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await run.Value.RunAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );
    }
}
