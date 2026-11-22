using Abox.Cli;
using Abox.Features.Run;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Launchers;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Tests;

public sealed class LauncherRegistryTests
{
    [Fact]
    public async Task DuplicateLauncherIdsAreReportedAsAnAboxErrorDuringComposition()
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var paths = AboxPaths.For(root.Path, root.Path);
        var application = new CliTestApplication(console, paths, new TrailingArguments());
        application.Services.AddScoped<ILauncher, ShellLauncher>();

        // Act
        var exitCode = await CommandHandling.RunAsync(
            async () =>
            {
                using (application)
                    return await application
                        .Provider.GetRequiredService<AboxRootCommand>()
                        .InvokeAsync(["--help"]);
            },
            console
        );

        // Assert
        exitCode.Should().Be(1);
        console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("error: ")
            .And.Contain("shell")
            .And.Contain("duplicate");
    }
}
