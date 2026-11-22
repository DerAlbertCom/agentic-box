using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;

namespace Abox.Features.UserSecrets;

/// <summary>
/// <c>abox user-secrets</c>: copies the .NET user-secret stores the folder configuration
/// selects into the existing sandbox of the folder.
/// </summary>
internal sealed class UserSecretsCommand : AboxCommand<UserSecretsParameters>
{
    private readonly LocationOption _location = new();
    private readonly Option<bool> _force = new("--force")
    {
        Description = "Override only the global user-secret value deny list.",
    };

    public UserSecretsCommand(CommandRunner runner)
        : base(
            "user-secrets",
            "Copy explicitly selected .NET user-secret stores into this folder's existing sandbox.",
            runner
        )
    {
        Options.Add(_location);
        Options.Add(_force);
    }

    internal override async Task<UserSecretsParameters> BindAsync(
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
        if (await location.FolderConfigurationAsync(cancellationToken) is null)
            throw new AboxException(
                $"No {FolderConfig.FileNames} in this folder. Declare 'userSecrets' explicitly before copying."
            );
        return new UserSecretsParameters(location, parseResult.GetValue(_force));
    }
}
