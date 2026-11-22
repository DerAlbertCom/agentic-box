using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Secrets;

/// <summary>
/// <c>abox secrets</c>: the secrets of the host that the kits of a folder's sandbox ask for.
/// </summary>
public sealed class SecretsCommand : Command
{
    public SecretsCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        Lazy<SecretsService> secrets,
        IConsole console,
        IYamlParser parser
    )
        : base("secrets", "Manage the host secrets the kits of a folder's sandbox ask for.")
    {
        Subcommands.Add(
            SubCommand(
                config,
                preflight,
                console,
                parser,
                "list",
                "List the services of the sandbox and whether they are stored.",
                withService: false,
                (request, cancellationToken) => secrets.Value.ListAsync(request, cancellationToken)
            )
        );
        Subcommands.Add(
            SubCommand(
                config,
                preflight,
                console,
                parser,
                "set",
                "Ask for the secrets of the sandbox and store them on the host.",
                withService: true,
                (request, cancellationToken) => secrets.Value.SetAsync(request, cancellationToken)
            )
        );
        Subcommands.Add(
            SubCommand(
                config,
                preflight,
                console,
                parser,
                "remove",
                "Remove the secrets of the sandbox from the host.",
                withService: true,
                (request, cancellationToken) =>
                    secrets.Value.RemoveAsync(request, cancellationToken)
            )
        );
    }

    private static Command SubCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        IConsole console,
        IYamlParser parser,
        string name,
        string description,
        bool withService,
        Func<SecretsRequest, CancellationToken, Task<int>> action
    )
    {
        var location = new Option<string>("--location")
        {
            Description = "Sandbox folder. Defaults to defaultLocation, then the current folder.",
        };
        var service = new Option<string[]>("--service")
        {
            Description = "The service to act on. Can be given more than once.",
            AllowMultipleArgumentsPerToken = false,
        };
        var command = new Command(name, description);
        command.Options.Add(location);
        if (withService)
        {
            command.Options.Add(service);
        }

        command.SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        var folder = config.Value.ResolveLocation(parseResult.GetValue(location));
                        var folderConfig = await FolderConfig.LoadAsync(
                            folder,
                            parser,
                            cancellationToken
                        );
                        var wanted = withService ? parseResult.GetValue(service) ?? [] : [];
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                            return 1;
                        return await action(
                            new SecretsRequest(folder, folderConfig, wanted),
                            cancellationToken
                        );
                    },
                    console
                )
        );
        return command;
    }
}
