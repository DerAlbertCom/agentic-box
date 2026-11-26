using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using NSubstitute;

namespace Abox.Tests;

public sealed class SourceRootResolverTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly string _outside;
    private readonly string _project;

    public SourceRootResolverTests()
    {
        _outside = _root.CreateDirectory("outside");
        _project = _root.CreateDirectory("project");
        _root.File("project/teams/sample/ac.abox", "workload: shared\n");
        _root.CreateDirectory("project/src/deep");
    }

    public void Dispose()
    {
        _root.Dispose();
    }

    private AboxPaths Paths => AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path);

    private SourceRootResolver Resolver(TestConsole console, string? sourceRoot = null)
    {
        var config = new AboxConfig { SourceRoot = sourceRoot };
        return new SourceRootResolver(config, Paths, console, Substitute.For<IYamlParser>());
    }

    [Fact]
    public void TheRootOptionWinsOverEverythingElse()
    {
        // Arrange
        var console = new TestConsole();
        var resolver = Resolver(console, sourceRoot: _project);

        // Act
        var root = resolver.Resolve("given", _outside, ["sample"]);

        // Assert
        root.Should().Be("given");
        console.Output.Should().BeEmpty();
    }

    [Fact]
    public void TheCurrentDirectoryWinsWhenTheTeamSourcesAreFoundFromIt()
    {
        // Arrange
        var nested = Path.Combine(_project, "src", "deep");
        var resolver = Resolver(new TestConsole(), sourceRoot: _outside);

        // Act
        var root = resolver.Resolve(null, nested, ["sample"]);

        // Assert
        root.Should().Be(nested);
    }

    [Fact]
    public void OutsideTheRepositoryTheSourceRootSettingIsUsed()
    {
        // Arrange
        var resolver = Resolver(new TestConsole(), sourceRoot: _project);

        // Act
        var root = resolver.Resolve(null, _outside, ["sample"]);

        // Assert
        root.Should().Be(_project);
    }

    [Fact]
    public void ASourceRootWithoutTheTeamSourcesFails()
    {
        // Arrange
        var resolver = Resolver(new TestConsole(), sourceRoot: _outside);

        // Act
        var action = () => resolver.Resolve(null, _outside, ["sample"]);

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                $"The 'sourceRoot' of the user configuration, '{_outside}', has no team sources for 'sample'. Fix 'sourceRoot' or give --root <directory>."
            );
    }

    [Fact]
    public void WithoutASourceRootAnInteractiveRunAsksOnceAndSavesTheFolder()
    {
        // Arrange
        Directory.CreateDirectory(Paths.ConfigDirectory);
        File.WriteAllText(Paths.ConfigFile, "defaultTeam: sample");
        var console = new TestConsole();
        console.Lines.Add(_project);
        var resolver = Resolver(console);

        // Act
        var root = resolver.Resolve(null, _outside, ["sample"]);

        // Assert
        root.Should().Be(_project);
        console.Output.Should().ContainSingle(line => line.Contains("team sources"));
        File.ReadAllText(Paths.ConfigFile)
            .Should()
            .Be($"defaultTeam: sample\nsourceRoot: '{_project.Replace("'", "''")}'\n");
    }

    [Fact]
    public void AnAnsweredFolderWithoutTheTeamSourcesIsNotSaved()
    {
        // Arrange
        var console = new TestConsole();
        console.Lines.Add(_outside);
        var resolver = Resolver(console);

        // Act
        var action = () => resolver.Resolve(null, _outside, ["sample"]);

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage($"'{_outside}' has no team sources for 'sample'.*");
        File.Exists(Paths.ConfigFile).Should().BeFalse();
    }

    [Fact]
    public void WithoutAnInteractiveTerminalTheRunFailsAndNamesTheSettingAndTheOption()
    {
        // Arrange
        var resolver = Resolver(new TestConsole(interactive: false));

        // Act
        var action = () => resolver.Resolve(null, _outside, ["sample"]);

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                "No team sources for 'sample' are found from the current directory. Give --root <directory> or set 'sourceRoot' in the user configuration."
            );
    }
}
