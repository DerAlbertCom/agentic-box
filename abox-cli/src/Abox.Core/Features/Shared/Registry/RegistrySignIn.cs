using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;

namespace Abox.Features.Shared.Registry;

/// <summary>Makes sure the host is signed in to the abox registry before it is used.</summary>
internal sealed class RegistrySignIn(IRegistryLogin login, IConsole console)
{
    private const string Registry = RegistryImageName.Registry;

    /// <summary>
    /// Returns when the stored sign-in is accepted. Otherwise it starts the sign-in on a
    /// terminal, and fails without one or when the sign-in fails.
    /// </summary>
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (await login.IsSignedInAsync(cancellationToken))
            return;

        if (!console.IsInteractive)
            throw new AboxException(
                $"Docker is not signed in to {Registry}. Run 'docker login {Registry}' with your JFrog user and identity token, then try again."
            );

        console.WriteLine(
            $"Docker is not signed in to {Registry}. Sign in with your JFrog user and identity token."
        );
        if (!await login.SignInAsync(cancellationToken))
            throw new AboxException(
                $"Signing in to {Registry} failed. Run 'docker login {Registry}' with your JFrog user and identity token, then try again."
            );
    }
}
