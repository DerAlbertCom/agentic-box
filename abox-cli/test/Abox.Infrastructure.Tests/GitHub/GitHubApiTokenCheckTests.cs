using System.Net;
using Abox.Infrastructure.GitHub;

namespace Abox.Tests;

public sealed class GitHubApiTokenCheckTests
{
    private const string Token = "github_pat_11ABCDEFG0123456789_secretpart";

    [Fact]
    public async Task AnAcceptedTokenTellsWhenItExpires()
    {
        // Arrange
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add(
                "github-authentication-token-expiration",
                "2026-12-27 10:00:00 UTC"
            );
            return response;
        });

        // Act
        var result = await Check(handler);

        // Assert
        result.Accepted.Should().BeTrue();
        result.ExpiresAt.Should().Be(new DateTimeOffset(2026, 12, 27, 10, 0, 0, TimeSpan.Zero));
        handler.Request!.RequestUri.Should().Be(new Uri("https://api.tenant.ghe.com/user"));
        handler.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Request.Headers.Authorization.Parameter.Should().Be(Token);
        handler.Request.Headers.UserAgent.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AnExpiryWithAnOffsetIsReadToo()
    {
        // Arrange
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add(
                "github-authentication-token-expiration",
                "2026-12-27 10:00:00 -0700"
            );
            return response;
        });

        // Act
        var result = await Check(handler);

        // Assert
        result.ExpiresAt.Should().Be(new DateTimeOffset(2026, 12, 27, 17, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ATokenWithoutExpiryIsAccepted()
    {
        // Act
        var result = await Check(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        // Assert
        result.Accepted.Should().BeTrue();
        result.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task AnUnauthorizedTokenIsRejectedAsWrongExpiredOrRevoked()
    {
        // Act
        var result = await Check(
            new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))
        );

        // Assert
        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("wrong, expired or revoked (401)");
    }

    [Fact]
    public async Task AnotherAnswerIsRejectedWithItsStatus()
    {
        // Act
        var result = await Check(
            new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))
        );

        // Assert
        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("403");
    }

    [Fact]
    public async Task AnUnreachableHostIsRejectedWithoutTheToken()
    {
        // Act
        var result = await Check(
            new FakeHandler(_ => throw new HttpRequestException($"connection refused for {Token}"))
        );

        // Assert
        result.Accepted.Should().BeFalse();
        result.Reason.Should().Contain("api.tenant.ghe.com could not be reached");
        result.Reason.Should().NotContain(Token);
    }

    private static Task<Abox.Features.Shared.GitHubTokens.GitHubTokenCheck> Check(
        FakeHandler handler
    ) =>
        new GitHubApiTokenCheck(new HttpClient(handler)).CheckAsync(
            "tenant.ghe.com",
            Token,
            CancellationToken.None
        );

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Request = request;
            return Task.FromResult(answer(request));
        }
    }
}
