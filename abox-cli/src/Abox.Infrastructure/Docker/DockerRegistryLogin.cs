using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Registry;

namespace Abox.Infrastructure.Docker;

/// <summary>
/// Checks and starts <c>docker login</c> for the abox registry. Docker keeps the credentials;
/// abox never sees them.
/// </summary>
internal sealed class DockerRegistryLogin(IProcessRunner runner) : IRegistryLogin
{
    private static readonly string[] Login = ["login", RegistryImageName.Registry];

    // With an empty stdin, docker login only tries the stored credentials: it cannot prompt
    // and fails when the registry does not accept them or none are stored.
    public async Task<bool> IsSignedInAsync(CancellationToken cancellationToken) =>
        (
            await runner.RunAsync(
                new ProcessRequest(DockerCommand.Program, Login, StandardInput: ""),
                cancellationToken
            )
        ).Succeeded;

    public async Task<bool> SignInAsync(CancellationToken cancellationToken) =>
        (
            await runner.RunAsync(
                new ProcessRequest(DockerCommand.Program, Login, AttachConsole: true),
                cancellationToken
            )
        ).Succeeded;
}
