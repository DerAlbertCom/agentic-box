using System.CommandLine;
using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Build;

/// <summary>
/// <c>abox build</c>: builds the component images of a team's squads, every squad of it
/// that no other squad names as its base, into the host image store.
/// </summary>
internal sealed class BuildCommand : AboxCommand<BuildParameters>
{
    private readonly Option<string[]> _teams = new("--team")
    {
        Description =
            "The team to build. Can be given more than once. Defaults to the team of the user configuration.",
        AllowMultipleArgumentsPerToken = false,
    };
    private readonly Option<string> _root = new("--root")
    {
        Description =
            "The directory to start searching for team sources. Defaults to the current directory when team sources are found from there, otherwise to the sourceRoot of the user configuration.",
    };
    private readonly Option<string> _tag = new("--tag")
    {
        Description =
            "The tag the build is stored under. Defaults to the tag of the user configuration, latest.",
    };
    private readonly Option<bool> _rebuild = new("--rebuild")
    {
        Description =
            "Build every component image again, also the ones whose inputs did not change.",
    };

    public BuildCommand(CommandRunner runner)
        : base(
            "build",
            "Build the component images of a team's squads: workload, local kits, state and configuration.",
            runner
        )
    {
        Options.Add(_teams);
        Options.Add(_root);
        Options.Add(_tag);
        Options.Add(_rebuild);
    }

    internal override Task<BuildParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    )
    {
        var tag = defaults.Tag(parseResult.GetValue(_tag));
        var teams = defaults.Teams(parseResult.GetValue(_teams));
        var root = Path.GetFullPath(defaults.SourceRoot(parseResult.GetValue(_root), teams));
        return Task.FromResult(
            new BuildParameters(teams, root, tag, parseResult.GetValue(_rebuild))
        );
    }
}
