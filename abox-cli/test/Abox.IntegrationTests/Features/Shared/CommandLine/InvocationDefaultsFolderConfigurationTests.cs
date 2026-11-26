using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class InvocationDefaultsFolderConfigurationTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly IYamlParser _parser = Substitute.For<IYamlParser>();
    private readonly InvocationDefaults _defaults;

    public InvocationDefaultsFolderConfigurationTests()
    {
        var yaml = new YamlDotNetParser();
        _parser
            .Parse(Arg.Any<string>(), Arg.Any<string>())
            .Returns(call => yaml.Parse(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        _defaults = new InvocationDefaults(
            new Lazy<AboxConfig>(() => new AboxConfig()),
            _parser,
            new TrailingArguments(),
            new Lazy<SourceRootResolver>(() =>
                throw new InvalidOperationException("No source root is used.")
            )
        );
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task TheFolderConfigurationOfTheSandboxLocationIsLoadedOnce()
    {
        // Arrange
        _root.File(FolderConfig.SharedFile, "squad: ac\nportOffset: 20000\n");

        // Act
        var location = await _defaults.SandboxLocationAsync(
            _root.Path,
            FolderConfigurationLoading.Now,
            CancellationToken.None
        );
        _root.File(FolderConfig.SharedFile, "squad: changed\n");
        var folderConfiguration = await location.FolderConfigurationAsync(CancellationToken.None);

        // Assert
        location.FullPath.Should().Be(_root.Path);
        folderConfiguration!.Squad.Should().Be("ac");
        folderConfiguration.PortOffset.Should().Be(20000);
        _parser.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task AFolderConfigurationLoadedOnFirstUseIsLoadedWhenFirstAskedForAndThenOnce()
    {
        // Arrange
        _root.File(FolderConfig.SharedFile, "squad: before\n");
        var location = await _defaults.SandboxLocationAsync(
            _root.Path,
            FolderConfigurationLoading.OnFirstUse,
            CancellationToken.None
        );
        _root.File(FolderConfig.SharedFile, "squad: ac\n");

        // Act
        var first = await location.FolderConfigurationAsync(CancellationToken.None);
        _root.File(FolderConfig.SharedFile, "squad: changed\n");
        var second = await location.FolderConfigurationAsync(CancellationToken.None);

        // Assert
        first!.Squad.Should().Be("ac");
        second.Should().BeSameAs(first);
        _parser.ReceivedCalls().Should().ContainSingle();
    }
}
