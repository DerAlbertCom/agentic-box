using System.CommandLine;
using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Secrets;

/// <summary>
/// <c>abox secrets</c>: the secrets of the host that the kits of a folder's sandbox ask for.
/// </summary>
internal sealed class SecretsCommand : Command
{
    public SecretsCommand(CommandRunner runner)
        : base("secrets", "Manage the host secrets the kits of a folder's sandbox ask for.")
    {
        Subcommands.Add(new ListCommand(runner));
        Subcommands.Add(new SetCommand(runner));
        Subcommands.Add(new RemoveCommand(runner));
    }

    /// <summary>The <c>--service</c> option that <c>set</c> and <c>remove</c> share.</summary>
    private static Option<string[]> ServiceOption() =>
        new("--service")
        {
            Description = "The service to act on. Can be given more than once.",
            AllowMultipleArgumentsPerToken = false,
        };

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
