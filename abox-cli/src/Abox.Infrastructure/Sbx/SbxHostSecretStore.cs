using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Squads;

namespace Abox.Infrastructure.Sbx;

/// <summary>The host secrets through <c>sbx secret</c>.</summary>
internal sealed class SbxHostSecretStore(SbxInvoker invoker) : IHostSecretStore
{
    public async Task<HostSecrets> ListAsync(CancellationToken cancellationToken)
    {
        var list = await invoker.RunJsonAsync<SbxSecrets>(
            ["secret", "ls", "--json"],
            "sbx secret ls",
            cancellationToken
        );
        return new HostSecrets(
            list.Secrets.Select(secret => secret.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList(),
            list.CustomSecrets.Select(secret => new CustomSecret(
                    secret.Targets.Where(target => !string.IsNullOrEmpty(target)).ToList(),
                    secret.EnvironmentVariable
                ))
                .ToList()
        );
    }

    /// <summary>The value goes through the standard input, so it stays out of the process list.</summary>
    public Task<HostSecretChange> SetAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    ) =>
        RunAsync(
            invoker.Call(["secret", "set", service]) with
            {
                StandardInput = value,
                UseUtf8StandardInput = true,
            },
            cancellationToken
        );

    /// <summary>
    /// The value goes through the standard input, so it stays out of the process list.
    /// Without a value, sbx takes it from the command of the declaration.
    /// </summary>
    public Task<HostSecretChange> SetCustomAsync(
        SecretDeclaration declaration,
        string? value,
        CancellationToken cancellationToken
    )
    {
        var arguments = SbxCustomSecretArguments.For(declaration);

        if (value is null)
        {
            arguments.Add("--command");
            arguments.Add(
                declaration.Command?.Line
                    ?? throw new ArgumentException(
                        $"The secret '{declaration.Service}' has neither a value nor a command.",
                        nameof(value)
                    )
            );
            return RunAsync(invoker.Call(arguments), cancellationToken);
        }

        arguments.Add("--value-stdin");

        return RunAsync(
            invoker.Call(arguments) with
            {
                StandardInput = value,
                UseUtf8StandardInput = true,
            },
            cancellationToken
        );
    }

    /// <summary>
    /// A secret of a service sbx does not know is removed by its target host, or by its
    /// environment variable when it has no host.
    /// </summary>
    public Task<HostSecretChange> RemoveAsync(
        SecretIdentity identity,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string> { "secret", "rm" };

        var (kind, value) = identity;
        if (kind == SecretIdentityKind.Host)
        {
            arguments.Add("--host");
        }
        else if (kind == SecretIdentityKind.EnvironmentVariable)
        {
            arguments.Add("--env-name");
        }

        arguments.Add(value);
        arguments.Add("--force");

        return RunAsync(invoker.Call(arguments), cancellationToken);
    }

    /// <summary>The <c>sbx secret set-custom</c> command line, quoted for a POSIX shell.</summary>
    public string ManualCustomSecretInstruction(
        SecretDeclaration declaration,
        string hostCommand
    ) => SbxCustomSecretArguments.CommandLine(declaration, hostCommand);

    private async Task<HostSecretChange> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        var outcome = await invoker.RunOutcomeAsync(request, cancellationToken);
        return new HostSecretChange(outcome.Succeeded, outcome.Output.Trim());
    }
}
