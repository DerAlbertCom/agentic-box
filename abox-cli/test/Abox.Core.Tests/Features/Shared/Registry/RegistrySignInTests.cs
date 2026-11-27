using Abox.Features.Shared.Common;
using Abox.Features.Shared.Registry;
using NSubstitute;

namespace Abox.Tests;

public sealed class RegistrySignInTests
{
    private readonly IRegistryLogin _login = Substitute.For<IRegistryLogin>();

    private Task EnsureAsync(bool interactive, TestConsole? console = null) =>
        new RegistrySignIn(_login, console ?? new TestConsole(interactive)).EnsureAsync(
            CancellationToken.None
        );

    [Fact]
    public async Task AnAcceptedSignInStartsNoNewOne()
    {
        // Arrange
        _login.IsSignedInAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await EnsureAsync(interactive: true);

        // Assert
        await _login.DidNotReceive().SignInAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutASignInATerminalUserIsLetSignIn()
    {
        // Arrange
        var console = new TestConsole(interactive: true);
        _login.SignInAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        await EnsureAsync(interactive: true, console);

        // Assert
        await _login.Received(1).SignInAsync(Arg.Any<CancellationToken>());
        console.AllText.Should().Contain("not signed in to rwest.jfrog.io");
    }

    [Fact]
    public async Task AFailedSignInStopsTheCommand()
    {
        // Arrange
        _login.SignInAsync(Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var action = () => EnsureAsync(interactive: true);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Signing in to rwest.jfrog.io failed. Run 'docker login rwest.jfrog.io'*"
        );
    }

    [Fact]
    public async Task WithoutATerminalTheUserIsToldToSignInThemselves()
    {
        // Act
        var action = () => EnsureAsync(interactive: false);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Docker is not signed in to rwest.jfrog.io. Run 'docker login rwest.jfrog.io'*"
        );
        await _login.DidNotReceive().SignInAsync(Arg.Any<CancellationToken>());
    }
}
