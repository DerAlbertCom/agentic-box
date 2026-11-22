using Abox.Features.Shared.SandboxCleanup;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class LegacySandboxPruneTests
{
    [Fact]
    public async Task PruneStillUsesActualInventoryNamesWithoutLocationResolution()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        string[] names = [cli.Name, LegacySandboxFixture.Name, "unrelated-running-sandbox"];
        var cleanup = Substitute.For<ISandboxCleanup>();
        cleanup
            .ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(names.Select(name => new SandboxSummary(name, true)).ToList());
        cleanup
            .ListTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TemplateImage>)[]);
        cleanup.StopSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        cli.Services.AddSingleton(cleanup);

        // Act
        var exitCode = await cli.Invoke("prune", "--sbx");

        // Assert
        exitCode.Should().Be(0);
        foreach (var name in names)
            await cleanup.Received(1).StopSandboxAsync(name, Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
