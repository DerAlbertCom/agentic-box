using System.CommandLine;
using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Publish;

/// <summary><c>abox publish</c>: publishes the local kit sets of a team to JFrog.</summary>
internal sealed class PublishCommand : AboxCommand<PublishParameters>
{
    private readonly Option<string[]> _teams = new("--team")
    {
        Description =
            "The team to publish. Can be repeated. Defaults to the team of the user configuration.",
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
            "The local build and registry tag. Defaults to the tag of the user configuration, latest.",
    };
    private readonly Option<string> _builder = new("--builder")
    {
        Description =
            "The Buildx builder to use. Defaults to Docker's selected builder (including BUILDX_BUILDER).",
    };

    public PublishCommand(CommandRunner runner)
        : base("publish", "Publish the local kit sets of a team to JFrog.", runner)
    {
        Options.Add(_teams);
        Options.Add(_root);
        Options.Add(_tag);
        Options.Add(_builder);
    }

    internal override Task<PublishParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    )
    {
        var teams = defaults.Teams(parseResult.GetValue(_teams));
        return Task.FromResult(
            new PublishParameters(
                teams,
                defaults.SourceRoot(parseResult.GetValue(_root), teams),
                defaults.TagWithoutValidation(parseResult.GetValue(_tag)),
                parseResult.GetValue(_builder)
            )
        );
    }
}
