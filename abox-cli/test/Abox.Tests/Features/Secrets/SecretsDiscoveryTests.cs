using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Which sandbox <c>abox secrets</c> works on: the one of the selected folder.</summary>
[Collection("Create command working directory")]
public sealed class SecretsDiscoveryTests
{
    [Theory]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("remove")]
    public async Task TheLocationOptionSelectsTheSandboxOfThatFolder(string action)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(new AboxConfig());
        var other = cli.Root.CreateDirectory("other");
        Sandboxes(cli, cli.Location, other);

        // Act
        var exitCode = await cli.Invoke("secrets", action, "--location", cli.Location);

        // Assert
        exitCode.Should().Be(0, cli.Console.AllText);
        await cli.SquadFiles.Received(1).ReadAsync(cli.Name, Arg.Any<CancellationToken>());
        await cli
            .SquadFiles.DidNotReceive()
            .ReadAsync(SandboxName.For(other), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutALocationTheDefaultLocationIsUsed()
    {
        // Arrange
        using var defaulted = new UserSecretsCliHarness();
        Sandboxes(defaulted, defaulted.Location);

        // Act
        var exitCode = await defaulted.Invoke("secrets", "list");

        // Assert
        exitCode.Should().Be(0, defaulted.Console.AllText);
        await defaulted
            .SquadFiles.Received(1)
            .ReadAsync(defaulted.Name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutALocationOrADefaultTheCurrentFolderIsUsed()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(new AboxConfig());
        Sandboxes(cli, cli.Location);
        var original = Directory.GetCurrentDirectory();

        // Act
        int exitCode;
        try
        {
            Directory.SetCurrentDirectory(cli.Location);
            exitCode = await cli.Invoke("secrets", "list");
        }
        finally
        {
            Directory.SetCurrentDirectory(original);
        }

        // Assert
        exitCode.Should().Be(0, cli.Console.AllText);
        await cli.SquadFiles.Received(1).ReadAsync(cli.Name, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("list")]
    [InlineData("set", "--service", "github")]
    [InlineData("remove", "--service", "github")]
    public async Task WithoutAFolderSandboxTheCreateRecoveryHintIsGivenAndNoSecretIsTouched(
        params string[] args
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness(new AboxConfig(), existingSandbox: false);
        cli.Lifecycle.ListAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<SandboxInfo>());
        cli.Provider.GetRequiredService<SandboxRecordStore>()
            .Save(
                cli.Name,
                new SandboxRecord
                {
                    Team = "recorded",
                    Squad = "ac",
                    Tag = "release",
                    KitReference = "published-set",
                }
            );
        cli.Console.Answers.Add("the-token");

        // Act
        var exitCode = await cli.Invoke(["secrets", .. args, "--location", cli.Location]);

        // Assert
        exitCode.Should().Be(1);
        cli.Console.AllText.Should()
            .Contain(
                $"There is no sandbox for '{cli.Location}'; secrets never creates or replaces one."
            )
            .And.Contain(
                $"Create it with: abox create --team recorded --squad ac --tag release --location \"{cli.Location}\""
            );
        await cli
            .SquadFiles.DidNotReceive()
            .ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cli.HostSecrets.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await cli
            .HostSecrets.DidNotReceive()
            .RemoveAsync(Arg.Any<SecretIdentity>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("--team", "sample")]
    [InlineData("--squad", "ac")]
    [InlineData("--root", ".")]
    public async Task TheTeamSourceOptionsAreNoLongerAccepted(string option, string value)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        Sandboxes(cli, cli.Location);

        // Act
        var exitCode = await cli.Invoke("secrets", "list", option, value);

        // Assert
        exitCode.Should().NotBe(0);
        await cli
            .SquadFiles.DidNotReceive()
            .ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static void Sandboxes(UserSecretsCliHarness cli, params string[] folders)
    {
        cli.Lifecycle.ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                folders
                    .Select(folder => new SandboxInfo(SandboxName.For(folder), "running", null))
                    .ToList()
            );
        cli.SquadFiles.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new SandboxSquadFiles(
                    [],
                    """{"SchemaVersion":1,"Secrets":[{"Service":"github"}]}"""
                )
            );
        cli.Console.Answers.Add("the-token");
    }
}
