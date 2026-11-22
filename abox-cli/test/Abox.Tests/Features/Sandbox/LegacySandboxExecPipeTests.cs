using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Sandboxes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class LegacySandboxExecPipeTests
{
    [Theory]
    [InlineData("exec")]
    [InlineData("pipe")]
    public async Task ExplicitNamesRemainExactAndNeverDiscoverAnotherSandbox(string command)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Provider.GetRequiredService<TrailingArguments>().Values = ["echo", "hello"];
        cli.Exec.RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxExecOutcome(23));

        // Act
        var exitCode = await cli.Invoke(command, LegacySandboxFixture.Name);

        // Assert
        exitCode.Should().Be(23);
        await cli
            .Exec.Received(1)
            .RunAsync(
                Arg.Is<SandboxExecRequest>(request =>
                    request.Sandbox == LegacySandboxFixture.Name
                    && request.Input
                        == (command == "pipe" ? SandboxExecInput.Forwarded : SandboxExecInput.None)
                ),
                Arg.Any<CancellationToken>()
            );
        await cli.Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await cli.Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }
}
