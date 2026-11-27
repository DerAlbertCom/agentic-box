using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.GitHubTokens;
using Abox.Features.Shared.Squads;
using NSubstitute;

namespace Abox.Tests;

public sealed class GitHubTokenSignInTests : IDisposable
{
    private const string Token = "github_pat_11ABCDEFG0123456789_abcdefghijklmnopqrstuvwxyz";

    private const string Service = "abox-github-enterprise";

    private static readonly SecretDeclaration Declaration = new(Service);

    private static readonly IReadOnlyList<string> Domains =
    [
        "tenant.ghe.com",
        "api.tenant.ghe.com",
        "copilot-api.tenant.ghe.com",
    ];

    private readonly TempDirectory _state = new();
    private readonly IHostSecretStore _store = Substitute.For<IHostSecretStore>();
    private readonly IGitHubTokenCheck _check = Substitute.For<IGitHubTokenCheck>();
    private readonly FixedClock _clock = new(
        new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero)
    );

    public GitHubTokenSignInTests()
    {
        _store
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(HostSecretChange.Done);
    }

    public void Dispose() => _state.Dispose();

    [Fact]
    public async Task WithoutATerminalTheCommandStopsWithThePointerToTheSetup()
    {
        // Arrange
        var console = new TestConsole(interactive: false);

        // Act
        var act = () => SetAsync(console);

        // Assert
        var error = (await act.Should().ThrowAsync<AboxException>()).Which.Message;
        error.Should().Contain("docs/github-enterprise-token.md");
        error.Should().Contain("abox secrets set --service abox-github-enterprise");
    }

    [Fact]
    public async Task AnAcceptedTokenFromThePrefilledPageIsStoredUnderTheServiceName()
    {
        // Arrange
        var console = new TestConsole { Answers = { Token } };
        Accept(Token, expiresAt: null);

        // Act
        var setting = await SetAsync(console);

        // Assert
        setting.Result.Should().Be(SecretSettingResult.Stored);
        await _store.Received(1).SetAsync(Service, Token, Arg.Any<CancellationToken>());
        console.AllText.Should().NotContain(Token);
    }

    [Fact]
    public async Task TheTenantIsTheDomainWithoutAnApiPrefixWhateverTheOrderOfTheDomains()
    {
        // Arrange
        var console = new TestConsole { Answers = { Token } };
        Accept(Token, expiresAt: null);
        IReadOnlyList<string> reordered =
        [
            "copilot-api.tenant.ghe.com",
            "api.tenant.ghe.com",
            "uploads.tenant.ghe.com",
            "tenant.ghe.com",
        ];

        // Act
        var setting = await new SecretValues(_store, console, SignIn(console)).SetAsync(
            Declaration,
            reordered,
            new HostSecrets([], []),
            CancellationToken.None
        );

        // Assert
        setting.Result.Should().Be(SecretSettingResult.Stored);
        await _check.Received(1).CheckAsync("tenant.ghe.com", Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheExpiryOfAnAcceptedTokenIsShownAndWarnedAboutLater()
    {
        // Arrange
        var console = new TestConsole { Answers = { Token } };
        Accept(Token, _clock.Now.AddDays(30));
        await SetAsync(console);
        _clock.Now = _clock.Now.AddDays(25);

        // Act
        Expiries(console).WarnWhenClose();

        // Assert
        console
            .Output.Should()
            .Contain("tenant.ghe.com accepted the token. It expires on 2026-12-27.");
        console.Errors.Should().ContainSingle().Which.Should().Contain("expires on 2026-12-27");
    }

    [Fact]
    public async Task ARejectedTokenIsAskedForAgain()
    {
        // Arrange
        const string expired = "github_pat_expired";
        var console = new TestConsole { Answers = { expired, Token } };
        _check
            .CheckAsync("tenant.ghe.com", expired, Arg.Any<CancellationToken>())
            .Returns(GitHubTokenCheck.Reject("the token is expired or revoked (401)."));
        Accept(Token, expiresAt: null);

        // Act
        var setting = await SetAsync(console);

        // Assert
        setting.Result.Should().Be(SecretSettingResult.Stored);
        console.AllText.Should().Contain("expired or revoked (401)");
        await _store.Received(1).SetAsync(Service, Token, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AClassicTokenIsRefusedWithoutAskingTheHost()
    {
        // Arrange
        var console = new TestConsole(answer: false) { Answers = { "gho_classic" } };

        // Act
        var setting = await SetAsync(console);

        // Assert
        setting.Result.Should().Be(SecretSettingResult.Skipped);
        console.AllText.Should().Contain("not a fine-grained personal access token");
        await _check
            .DidNotReceive()
            .CheckAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _store
            .DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePermissionsAndThePrefilledLinkArePrintedButNoTokenIsCreated()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        var setting = await SetAsync(console);

        // Assert
        setting.Result.Should().Be(SecretSettingResult.Skipped);
        console.Output.Should().Contain("  contents: write (clone, pull, push, branches, tags)");
        console.Output.Should().Contain(line => line.StartsWith("  administration: none"));
        console
            .Output.Where(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Take(3)
            .Should()
            .Equal(
                "  actions: write (dispatch workflows, read runs and logs)",
                "  administration: none (the token cannot create or delete repositories)",
                "  statuses: read (see CI results)"
            );
        var link = console.Output.Single(line => line.StartsWith("This link prefills them: "))[
            "This link prefills them: ".Length..
        ];
        var page = new Uri(link);
        page.Host.Should().Be("tenant.ghe.com");
        page.AbsolutePath.Should().Be("/settings/personal-access-tokens/new");
        page.Query.Should().Contain("copilot_requests=write").And.NotContain("administration");
        console.Output.Should().Contain("How to create it is in docs/github-enterprise-token.md.");
    }

    [Fact]
    public async Task WithoutAKitThatNamesItsDomainsTheServiceIsAskedForLikeAnyOther()
    {
        // Arrange
        var console = new TestConsole { Answers = { "plain-value" } };

        // Act
        var setting = await new SecretValues(_store, console, SignIn(console)).SetAsync(
            Declaration,
            [],
            new HostSecrets([], []),
            CancellationToken.None
        );

        // Assert
        setting.Result.Should().Be(SecretSettingResult.Stored);
        console.Output.Should().NotContain(line => line.StartsWith("This link prefills them"));
        await _store.Received(1).SetAsync(Service, "plain-value", Arg.Any<CancellationToken>());
    }

    private void Accept(string token, DateTimeOffset? expiresAt) =>
        _check
            .CheckAsync("tenant.ghe.com", token, Arg.Any<CancellationToken>())
            .Returns(GitHubTokenCheck.Accept(expiresAt));

    private Task<SecretSetting> SetAsync(TestConsole console) =>
        new SecretValues(_store, console, SignIn(console)).SetAsync(
            Declaration,
            Domains,
            new HostSecrets([], []),
            CancellationToken.None
        );

    private GitHubTokenSignIn SignIn(IConsole console) => new(_check, Expiries(console), console);

    private GitHubTokenExpiries Expiries(IConsole console) =>
        new(AboxPaths.For(_state.Path, _state.Path), _clock, console);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
