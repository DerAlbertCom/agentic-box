using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class InvocationDefaultsFolderConfigurationTests
{
    [Fact]
    public async Task TheFolderConfigurationOfTheSandboxLocationIsLoadedOnce()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(FolderConfig.SharedFile, "squad: ac\nportOffset: 20000\n");
        var yaml = new YamlDotNetParser();
        var parser = Substitute.For<IYamlParser>();
        parser
            .Parse(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => yaml.Parse(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        var defaults = new InvocationDefaults(
            new Lazy<AboxConfig>(() => new AboxConfig()),
            parser,
            new TrailingArguments()
        );

        // Act
        var location = await defaults.SandboxLocationAsync(root.Path, CancellationToken.None);
        root.File(FolderConfig.SharedFile, "squad: changed\n");

        // Assert
        location.FullPath.Should().Be(root.Path);
        location.FolderConfiguration!.Squad.Should().Be("ac");
        location.FolderConfiguration.PortOffset.Should().Be(20000);
        parser.ReceivedCalls().Should().ContainSingle();
    }
}
