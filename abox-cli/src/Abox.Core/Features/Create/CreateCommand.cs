using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.Create;

/// <summary>
/// <c>abox create</c>: creates the sandbox of a folder from a published or local squad kit set.
/// </summary>
internal sealed class CreateCommand : AboxCommand<CreateParameters>
{
    private readonly Option<string> _team = new("--team")
    {
        Description =
            "The team the squad belongs to. Defaults to the team of the user configuration.",
    };
    private readonly Option<string> _squad = new("--squad")
    {
        Description =
            $"The squad the sandbox is created from. Defaults to the folder configuration ({FolderConfig.FileNames}); a conflicting --squad is rejected. Without either, --local can ask which squad to use.",
    };
    private readonly LocationOption _location = new();
    private readonly Option<string> _tag = new("--tag")
    {
        Description =
            "The tag of the published set or local build. Defaults to the tag of the user configuration.",
    };
    private readonly Option<string> _root = new("--root")
    {
        Description =
            "The directory to start searching for team sources with --local. Defaults to the resolved location.",
    };
    private readonly Option<bool> _local = new("--local")
    {
        Description =
            "Use the prebuilt local workload and v3 kits, without pulling a published squad set.",
    };
    private readonly Option<bool> _assumeYes = new("--yes")
    {
        Description = "Replace an existing sandbox without asking.",
    };

    public CreateCommand(CommandRunner runner)
        : base(
            "create",
            "Create the sandbox of a folder from a published squad kit set, or a local build with --local.",
            runner
        )
    {
        Options.Add(_team);
        Options.Add(_squad);
        Options.Add(_location);
        Options.Add(_tag);
        Options.Add(_root);
        Options.Add(_local);
        Options.Add(_assumeYes);
    }

    internal override async Task<CreateParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    )
    {
        var local = parseResult.GetValue(_local);
        var sourceRoot = parseResult.GetValue(_root);
        if (!local && sourceRoot is not null)
        {
            throw new AboxException(
                "--root requires --local; remote create does not use local team sources."
            );
        }

        var tag = defaults.Tag(parseResult.GetValue(_tag));
        var team = defaults.Team(parseResult.GetValue(_team));
        var location = await defaults.SandboxLocationAsync(
            parseResult.GetValue(_location),
            FolderConfigurationLoading.Now,
            cancellationToken
        );
        return new CreateParameters(
            location,
            team,
            parseResult.GetValue(_squad),
            tag,
            Path.GetFullPath(sourceRoot ?? location.FullPath),
            local,
            parseResult.GetValue(_assumeYes)
        );
    }
}
