using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.Secrets;

/// <summary>
/// <c>abox secrets</c>: the secrets of the host that the kits of a folder's sandbox ask for.
/// </summary>
internal sealed class SecretsCommand : Command
{
    public SecretsCommand(CommandRunner runner)
        : base("secrets", "Manage the host secrets the kits of a folder's sandbox ask for.")
    {
        Subcommands.Add(new InitCommand(runner));
        Subcommands.Add(new UpdateCommand(runner));
        Subcommands.Add(new ListCommand(runner));
        Subcommands.Add(new SetCommand(runner));
        Subcommands.Add(new RemoveCommand(runner));
    }

    /// <summary>The <c>--service</c> option that <c>update</c>, <c>set</c> and <c>remove</c> share.</summary>
    private static Option<string[]> ServiceOption() =>
        new("--service")
        {
            Description = "The service to act on. Can be given more than once.",
            AllowMultipleArgumentsPerToken = false,
        };

    private sealed class InitCommand : AboxCommand<SecretsInitParameters>
    {
        private readonly LocationOption _location = new();
        private readonly Option<string> _team = new("--team")
        {
            Description =
                $"The team of the squad. Defaults to the folder configuration, then the team of the user configuration.",
        };
        private readonly Option<string> _squad = new("--squad")
        {
            Description =
                $"The squad whose secrets are set up. Defaults to the folder configuration ({FolderConfig.FileNames}).",
        };
        private readonly Option<bool> _local = new("--local")
        {
            Description =
                "Read the squad from the local build of 'abox build' instead of the published set.",
        };
        private readonly Option<string> _tag = new("--tag")
        {
            Description =
                "The tag of the published set or local build. Defaults to the tag of the user configuration.",
        };
        private readonly Option<bool> _all = new("--all")
        {
            Description = "Ask for every secret again, also the stored ones.",
        };

        public InitCommand(CommandRunner runner)
            : base(
                "init",
                "Set up every secret a squad needs, before a sandbox exists: asks for the values that are not stored and the missing approvals.",
                runner
            )
        {
            Options.Add(_location);
            Options.Add(_team);
            Options.Add(_squad);
            Options.Add(_local);
            Options.Add(_tag);
            Options.Add(_all);
        }

        internal override async Task<SecretsInitParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        )
        {
            var tag = defaults.Tag(parseResult.GetValue(_tag));
            var location = await defaults.SandboxLocationAsync(
                parseResult.GetValue(_location),
                FolderConfigurationLoading.Now,
                cancellationToken
            );
            var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
            return new SecretsInitParameters(
                location,
                defaults.Team(parseResult.GetValue(_team) ?? folderConfig?.Team),
                parseResult.GetValue(_squad),
                tag,
                parseResult.GetValue(_local),
                parseResult.GetValue(_all)
            );
        }
    }

    private sealed class UpdateCommand : AboxCommand<SecretsUpdateParameters>
    {
        private readonly LocationOption _location = new();
        private readonly Option<string[]> _services = ServiceOption();

        public UpdateCommand(CommandRunner runner)
            : base(
                "update",
                "Pick secrets of the folder's sandbox from a list with their state and set them up again.",
                runner
            )
        {
            Options.Add(_location);
            Options.Add(_services);
        }

        internal override async Task<SecretsUpdateParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        )
        {
            var location = await defaults.SandboxLocationAsync(
                parseResult.GetValue(_location),
                FolderConfigurationLoading.Now,
                cancellationToken
            );
            var folderConfig = await location.FolderConfigurationAsync(cancellationToken);
            return new SecretsUpdateParameters(
                location,
                parseResult.GetValue(_services) ?? [],
                defaults.TeamOrNone(folderConfig?.Team),
                defaults.Tag(null)
            );
        }
    }

    private sealed class ListCommand : AboxCommand<SecretsListParameters>
    {
        private readonly LocationOption _location = new();

        public ListCommand(CommandRunner runner)
            : base("list", "List the services of the sandbox and whether they are stored.", runner)
        {
            Options.Add(_location);
        }

        internal override async Task<SecretsListParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        ) =>
            new(
                await defaults.SandboxLocationAsync(
                    parseResult.GetValue(_location),
                    FolderConfigurationLoading.Now,
                    cancellationToken
                )
            );
    }

    private sealed class SetCommand : AboxCommand<SecretsSetParameters>
    {
        private readonly LocationOption _location = new();
        private readonly Option<string[]> _services = ServiceOption();

        public SetCommand(CommandRunner runner)
            : base("set", "Ask for the secrets of the sandbox and store them on the host.", runner)
        {
            Options.Add(_location);
            Options.Add(_services);
        }

        internal override async Task<SecretsSetParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        ) =>
            new(
                await defaults.SandboxLocationAsync(
                    parseResult.GetValue(_location),
                    FolderConfigurationLoading.Now,
                    cancellationToken
                ),
                parseResult.GetValue(_services) ?? []
            );
    }

    private sealed class RemoveCommand : AboxCommand<SecretsRemoveParameters>
    {
        private readonly LocationOption _location = new();
        private readonly Option<string[]> _services = ServiceOption();

        public RemoveCommand(CommandRunner runner)
            : base("remove", "Remove the secrets of the sandbox from the host.", runner)
        {
            Options.Add(_location);
            Options.Add(_services);
        }

        internal override async Task<SecretsRemoveParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        ) =>
            new(
                await defaults.SandboxLocationAsync(
                    parseResult.GetValue(_location),
                    FolderConfigurationLoading.Now,
                    cancellationToken
                ),
                parseResult.GetValue(_services) ?? []
            );
    }
}
