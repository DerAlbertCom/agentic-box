using System.CommandLine;
using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Build;

/// <summary>
/// <c>abox build</c>: builds the kit set of a team, every squad of it that no other
/// squad names as its base.
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
    private readonly Option<bool> _noVerify = new("--no-verify")
    {
        Description =
            "Skip additional local kit inspection with sbx. Normal builds still build the workload image.",
    };
    private readonly Option<bool> _noPersonal = new("--no-personal")
    {
        Description = "Exclude personal .abox files to make a build safe to publish.",
    };
    private readonly Option<bool> _metadataOnly = new("--metadata-only")
    {
        Description =
            "Prepare source metadata without Docker or sbx (maintainers). Replaces the manifest and clears its local image selection; remote create does not need this.",
    };

    public BuildCommand(CommandRunner runner)
        : base(
            "build",
            "Build the kit set of a team: its workload and the kits of its squads.",
            runner
        )
    {
        Options.Add(_teams);
        Options.Add(_root);
        Options.Add(_tag);
        Options.Add(_noVerify);
        Options.Add(_noPersonal);
        Options.Add(_metadataOnly);
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
            new BuildParameters(
                teams,
                root,
                tag,
                !parseResult.GetValue(_noVerify),
                !parseResult.GetValue(_noPersonal),
                parseResult.GetValue(_metadataOnly)
            )
        );
    }
}
