using System.CommandLine;
using Abox.Features.Shared.CommandLine;

namespace Abox.Cli;

/// <summary>
/// The abox command line: the commands the app registered, one per slice.
/// </summary>
public sealed class AboxRootCommand : RootCommand
{
    private readonly Verbosity _verbosity;
    private readonly Option<bool> _verbose = new("--verbose")
    {
        Description =
            "Show complete captured tool stdout and stderr on stderr (with known secrets redacted).",
        Recursive = true,
    };

    public AboxRootCommand(IEnumerable<Command> subcommands, Verbosity verbosity)
        : base("abox: AI agent sandboxes for your folders.")
    {
        _verbosity = verbosity;
        Options.Add(_verbose);
        foreach (var subcommand in subcommands)
        {
            Subcommands.Add(subcommand);
        }
    }

    public Task<int> InvokeAsync(IReadOnlyList<string> arguments)
    {
        var parsed = this.Parse(arguments);
        _verbosity.Verbose = parsed.GetValue(_verbose);
        return parsed.InvokeAsync();
    }
}
