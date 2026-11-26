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
            $"The team the squad belongs to. Saved in {FolderConfig.UserFile} of the location; defaults to the folder configuration, then the team of the user configuration.",
    };
    private readonly Option<string> _squad = new("--squad")
    {
        Description =
            $"The squad the sandbox is created from. Saved in {FolderConfig.UserFile} of the location; defaults to the folder configuration ({FolderConfig.FileNames}). A squad that conflicts with {FolderConfig.SharedFile} is rejected. One of them is required.",
    };
    private readonly LocationOption _location = new();
    private readonly Option<string> _tag = new("--tag")
    {
        Description =
            "The tag of the published set or local build. Defaults to the tag of the user configuration.",
    };
    private readonly Option<bool> _local = new("--local")
    {
        Description =
            "Use the local build of 'abox build' instead of pulling a published squad set. No team sources are needed.",
    };
    private readonly Option<int?> _memory = new("--memory")
    {
        Description =
            $"The memory of the sandbox in GB. Saved in {FolderConfig.UserFile} of the location; without it, the folder configuration, then the sbx default.",
    };
    private readonly Option<int?> _cpus = new("--cpus")
    {
        Description =
            $"The number of CPUs of the sandbox. Saved in {FolderConfig.UserFile} of the location; without it, the folder configuration, then the sbx default.",
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
        Options.Add(_local);
        Options.Add(_memory);
        Options.Add(_cpus);
        Options.Add(_assumeYes);
    }

    internal override async Task<CreateParameters> BindAsync(
        ParseResult parseResult,
        InvocationDefaults defaults,
        CancellationToken cancellationToken
    )
    {
        var memory = AtLeastOne(parseResult.GetValue(_memory), "--memory");
        var cpus = AtLeastOne(parseResult.GetValue(_cpus), "--cpus");
        var tag = defaults.Tag(parseResult.GetValue(_tag));
        var location = await defaults.SandboxLocationAsync(
            parseResult.GetValue(_location),
            FolderConfigurationLoading.Now,
            cancellationToken
        );
        var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
        var team = defaults.Team(parseResult.GetValue(_team) ?? folderConfig?.Team);
        return new CreateParameters(
            location,
            team,
            parseResult.GetValue(_squad),
            tag,
            parseResult.GetValue(_local),
            parseResult.GetValue(_assumeYes),
            memory,
            cpus
        );
    }

    private static int? AtLeastOne(int? value, string option) =>
        value is < 1
            ? throw new AboxException($"{option} must be a whole number of at least 1.")
            : value;
}
