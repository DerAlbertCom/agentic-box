using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsCommandTests
{
    [Fact]
    public async Task ConfiguredDefaultLocationIsUsedWhenLocationIsOmitted()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.yaml", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");

        // Act
        var exitCode = await fixture.RunDefault();

        // Assert
        exitCode.Should().Be(0);
        await fixture.Lifecycle.Received(1).ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheUserFileAloneSelectsTheUserSecrets()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.ExistingSandbox();
        fixture.ConfigureForUser("userSecrets: [sample]\n");
        fixture.Store("sample", "{}");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        await fixture
            .UserSecrets.Received(1)
            .WriteUserSecretsAsync(fixture.Name, "sample", "{}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreflightFailureDoesNotReachTheDestination()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.yaml", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Preflight.CheckVersionAsync(Arg.Any<CancellationToken>())
            .Returns(ReadyPreflight.OldVersion);

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(1);
        await fixture.NoWrites();
        await fixture.Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelledCopyUsesExit130AndExplainsSafeRetryWithoutLeakingCancellationDetails()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.yaml", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .UserSecrets.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromException(
                    new OperationCanceledException("synthetic-password 123456789 true")
                )
            );

        // Act
        var exitCode = await fixture.Run("--verbose");

        // Assert
        exitCode.Should().Be(130);
        fixture
            .Console.AllText.Should()
            .Contain("retry")
            .And.Contain("rollback")
            .And.NotContain("synthetic-password")
            .And.NotContain("123456789 true");
    }

    [Fact]
    public async Task DoesNotCreateOrReplaceAMissingSandbox()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.yaml", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Lifecycle.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SandboxInfo>());

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("abox create");
        await fixture.NoWrites();
        await fixture
            .Lifecycle.DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        await fixture
            .Lifecycle.DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("policy")]
    [InlineData("transfer")]
    public async Task TransferAndTargetPreparationFailuresAreValueFreeAndForceDoesNotBypassThem(
        string failurePhase
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Configure("""{"squad":"ac","userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", "{}");
        if (failurePhase == "destination")
            fixture
                .UserSecrets.ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException(new AboxException("synthetic-password 123456789 true"))
                );
        else if (failurePhase == "policy")
            fixture
                .UserSecrets.GetUserSecretsHostPortsAsync(
                    fixture.Name,
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException<IReadOnlyList<int>>(
                        new AboxException("synthetic-password 123456789 true")
                    )
                );
        else
            fixture
                .UserSecrets.WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    "later",
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException(new AboxException("synthetic-password 123456789 true"))
                );

        // Act
        var exitCode = await fixture.Run("--force", "--verbose");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .NotContain("synthetic-password")
            .And.NotContain("123456789 true")
            .And.Contain("retry");
        if (failurePhase != "transfer")
            await fixture.NoWrites();
        else
            fixture.Console.AllText.Should().Contain("1 completed").And.Contain("rollback");
    }
}
