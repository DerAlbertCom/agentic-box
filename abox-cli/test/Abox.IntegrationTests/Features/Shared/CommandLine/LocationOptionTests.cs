using System.CommandLine;
using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Tests;

public sealed class LocationOptionTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("run")]
    [InlineData("secrets", "list")]
    [InlineData("secrets", "set")]
    [InlineData("secrets", "remove")]
    [InlineData("user-secrets")]
    public async Task TheLocationOptionHasTheSharedHelpText(params string[] command)
    {
        // Arrange
        using var root = new TempDirectory();
        using var application = new CliTestApplication(
            new TestConsole(),
            AboxPaths.For(root.Path, root.Path),
            new TrailingArguments()
        );
        using var output = new StringWriter();

        // Act
        await application
            .Provider.GetRequiredService<AboxRootCommand>()
            .Parse([.. command, "--help"])
            .InvokeAsync(new InvocationConfiguration { Output = output });

        // Assert
        string.Join(
                ' ',
                output.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            )
            .Should()
            .Contain(
                "--location <location> The folder of the sandbox. Defaults to defaultLocation in the user configuration, then the current folder."
            );
    }
}
