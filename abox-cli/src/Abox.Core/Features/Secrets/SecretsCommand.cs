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

    /// <summary>The options every subcommand shares and how they bind.</summary>
    private abstract class SubCommand<TParameters> : AboxCommand<TParameters>
        where TParameters : notnull
    {
        private readonly LocationOption _location = new();
        private readonly Option<string[]> _service = new("--service")
        {
            Description = "The service to act on. Can be given more than once.",
            AllowMultipleArgumentsPerToken = false,
        };
        private readonly bool _withService;

        protected SubCommand(
            string name,
            string description,
            bool withService,
            CommandRunner runner
        )
            : base(name, description, runner)
        {
            _withService = withService;
            Options.Add(_location);
            if (withService)
            {
                Options.Add(_service);
            }
        }

        internal sealed override async Task<TParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        )
        {
            var location = await defaults.SandboxLocationAsync(
                parseResult.GetValue(_location),
                cancellationToken
            );
            IReadOnlyList<string> services = _withService
                ? parseResult.GetValue(_service) ?? []
                : [];
            return Parameters(location, services);
        }

        protected abstract TParameters Parameters(
            SandboxLocation location,
            IReadOnlyList<string> services
        );
    }

    private sealed class ListCommand(CommandRunner runner)
        : SubCommand<SecretsListParameters>(
            "list",
            "List the services of the sandbox and whether they are stored.",
            withService: false,
            runner
        )
    {
        protected override SecretsListParameters Parameters(
            SandboxLocation location,
            IReadOnlyList<string> services
        ) => new(location);
    }

    private sealed class SetCommand(CommandRunner runner)
        : SubCommand<SecretsSetParameters>(
            "set",
            "Ask for the secrets of the sandbox and store them on the host.",
            withService: true,
            runner
        )
    {
        protected override SecretsSetParameters Parameters(
            SandboxLocation location,
            IReadOnlyList<string> services
        ) => new(location, services);
    }

    private sealed class RemoveCommand(CommandRunner runner)
        : SubCommand<SecretsRemoveParameters>(
            "remove",
            "Remove the secrets of the sandbox from the host.",
            withService: true,
            runner
        )
    {
        protected override SecretsRemoveParameters Parameters(
            SandboxLocation location,
            IReadOnlyList<string> services
        ) => new(location, services);
    }
}
