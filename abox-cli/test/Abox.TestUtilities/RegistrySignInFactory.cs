using Abox.Features.Shared.Common;
using Abox.Features.Shared.Registry;
using NSubstitute;

namespace Abox.Tests;

internal static class RegistrySignInFactory
{
    /// <summary>A sign-in whose stored credentials the registry accepts.</summary>
    public static RegistrySignIn SignedIn(IConsole? console = null)
    {
        var login = Substitute.For<IRegistryLogin>();
        login.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);
        return new RegistrySignIn(login, console ?? new TestConsole());
    }
}
