using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Yaml;
using NSubstitute;

namespace Abox.Tests;

public sealed class InvocationDefaultsTests
{
    private readonly IYamlParser _parser = Substitute.For<IYamlParser>();

    private InvocationDefaults Defaults(AboxConfig configuration) =>
        new(new Lazy<AboxConfig>(() => configuration), _parser);

    [Fact]
    public async Task TheSandboxLocationPrefersTheOptionOverTheDefaultLocation()
    {
        // Arrange
        using var root = new TempDirectory();
        var option = root.CreateDirectory("option");
        var defaults = Defaults(
            new AboxConfig { DefaultLocation = root.CreateDirectory("default") }
        );

        // Act
        var location = await defaults.SandboxLocationAsync(option, CancellationToken.None);

        // Assert
        location.FullPath.Should().Be(option);
    }

    [Fact]
    public async Task TheSandboxLocationFallsBackToTheDefaultLocation()
    {
        // Arrange
        using var root = new TempDirectory();
        var configured = root.CreateDirectory("default");
        var defaults = Defaults(new AboxConfig { DefaultLocation = configured });

        // Act
        var location = await defaults.SandboxLocationAsync(null, CancellationToken.None);

        // Assert
        location.FullPath.Should().Be(configured);
    }

    [Fact]
    public void TheSandboxLocationFallsBackToTheCurrentFolder()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig());

        // Act
        var location = defaults.SandboxLocationWithoutFolderConfiguration(null);

        // Assert
        location.FullPath.Should().Be(Directory.GetCurrentDirectory());
    }

    [Fact]
    public async Task TheSandboxLocationIsAFullPath()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig());
        var relative = Path.Combine("some", "..", "folder");

        // Act
        var location = await defaults.SandboxLocationAsync(relative, CancellationToken.None);

        // Assert
        location.FullPath.Should().Be(Path.Combine(Directory.GetCurrentDirectory(), "folder"));
    }

    [Fact]
    public async Task ALocationWithoutFolderConfigurationHasNone()
    {
        // Arrange
        using var root = new TempDirectory();
        var defaults = Defaults(new AboxConfig());

        // Act
        var location = await defaults.SandboxLocationAsync(root.Path, CancellationToken.None);

        // Assert
        location.FolderConfiguration.Should().BeNull();
    }

    [Fact]
    public void TheVariantWithoutFolderConfigurationDoesNotReadTheFolderConfiguration()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(FolderConfig.SharedFile, "squad: ac\n");
        var defaults = Defaults(new AboxConfig());

        // Act
        var location = defaults.SandboxLocationWithoutFolderConfiguration(root.Path);

        // Assert
        location.Should().Be(new SandboxLocation(root.Path, null));
        _parser.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void TheTeamPrefersTheOptionOverTheDefaultTeam()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTeam = "default" });

        // Act
        var team = defaults.Team("given");

        // Assert
        team.Should().Be("given");
    }

    [Fact]
    public void TheTeamFallsBackToTheDefaultTeam()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTeam = "default" });

        // Act
        var team = defaults.Team(null);

        // Assert
        team.Should().Be("default");
    }

    [Fact]
    public void WithoutATeamOrDefaultTeamTheTeamIsRequired()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig());

        // Act
        var action = () => defaults.Team(null);

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
            );
    }

    [Fact]
    public void TheTeamsKeepEveryGivenTeam()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTeam = "default" });

        // Act
        var teams = defaults.Teams(["one", "two"]);

        // Assert
        teams.Should().Equal("one", "two");
    }

    [Fact]
    public void WithoutGivenTeamsTheTeamsAreTheDefaultTeam()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTeam = "default" });

        // Act
        var teams = defaults.Teams([]);

        // Assert
        teams.Should().Equal("default");
    }

    [Fact]
    public void WithoutGivenTeamsOrDefaultTeamTheTeamsAreRequired()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig());

        // Act
        var action = () => defaults.Teams(null);

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
            );
    }

    [Fact]
    public void TheTagPrefersTheOptionOverTheDefaultTag()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTag = "release" });

        // Act
        var tag = defaults.Tag("given");

        // Assert
        tag.Should().Be("given");
    }

    [Fact]
    public void TheTagFallsBackToTheDefaultTag()
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTag = "release" });

        // Act
        var tag = defaults.Tag(null);

        // Assert
        tag.Should().Be("release");
    }

    [Theory]
    [InlineData("-leading-hyphen", null)]
    [InlineData(null, "")]
    public void AnInvalidTagIsRejected(string? given, string? configured)
    {
        // Arrange
        var defaults = Defaults(new AboxConfig { DefaultTag = configured ?? "latest" });

        // Act
        var action = () => defaults.Tag(given);

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage(
                $"'{given ?? configured}' is not a valid registry tag. Use 1-128 ASCII letters, digits, underscores, periods or hyphens, starting with a letter, digit or underscore."
            );
    }
}
