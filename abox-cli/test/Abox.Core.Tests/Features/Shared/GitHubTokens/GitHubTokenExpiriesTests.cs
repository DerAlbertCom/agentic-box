using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.GitHubTokens;

namespace Abox.Tests;

public sealed class GitHubTokenExpiriesTests : IDisposable
{
    private static readonly DateTimeOffset Today = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _state = new();
    private readonly TestConsole _console = new();

    public void Dispose() => _state.Dispose();

    [Fact]
    public void ATokenThatExpiresWithinAWeekIsWarnedAbout()
    {
        // Arrange
        Expiries().Record("github-enterprise", "tenant.ghe.com", Today.AddDays(3));

        // Act
        Expiries().WarnWhenClose();

        // Assert
        _console
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "the token of the secret 'github-enterprise' for tenant.ghe.com expires on 2026-11-30"
            );
        _console.Errors[0].Should().Contain("abox secrets set --service github-enterprise");
    }

    [Fact]
    public void AnExpiredTokenIsWarnedAbout()
    {
        // Arrange
        Expiries().Record("github-enterprise", "tenant.ghe.com", Today.AddDays(-1));

        // Act
        Expiries().WarnWhenClose();

        // Assert
        _console.Errors.Should().ContainSingle().Which.Should().Contain("expired on 2026-11-26");
    }

    [Fact]
    public void ATokenWithMoreThanAWeekLeftIsNotWarnedAbout()
    {
        // Arrange
        Expiries().Record("github-enterprise", "tenant.ghe.com", Today.AddDays(8));

        // Act
        Expiries().WarnWhenClose();

        // Assert
        _console.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ANewTokenWithoutExpiryReplacesTheOldOne()
    {
        // Arrange
        Expiries().Record("github-enterprise", "tenant.ghe.com", Today.AddDays(1));
        Expiries().Record("github-enterprise", "tenant.ghe.com", expiresAt: null);

        // Act
        Expiries().WarnWhenClose();

        // Assert
        _console.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AForgottenTokenIsNotWarnedAbout()
    {
        // Arrange
        Expiries().Record("github-enterprise", "tenant.ghe.com", Today.AddDays(1));
        Expiries().Forget("github-enterprise");

        // Act
        Expiries().WarnWhenClose();

        // Assert
        _console.Errors.Should().BeEmpty();
    }

    [Fact]
    public void WithoutRecordedTokensNothingIsWarnedAbout()
    {
        // Act
        Expiries().WarnWhenClose();

        // Assert
        _console.Errors.Should().BeEmpty();
    }

    private GitHubTokenExpiries Expiries() =>
        new(AboxPaths.For(_state.Path, _state.Path), new FixedClock(Today), _console);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
