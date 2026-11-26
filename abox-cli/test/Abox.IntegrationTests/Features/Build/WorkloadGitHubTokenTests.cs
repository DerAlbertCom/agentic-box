using Abox.Features.Build;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

[Collection("Docker build environment")]
public sealed class WorkloadGitHubTokenTests
{
    private readonly BuildServiceFixture _fixture = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("synthetic-github-build-token")]
    public async Task OnlyTheWorkloadImageReceivesAnAvailableGitHubTokenOnTheHostBuilder(
        string? token
    )
    {
        // Arrange
        _fixture.Root.File("teams/sample/ac.abox", "workload: sample\nkits: [private]\n");
        var previous = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", token);
        try
        {
            // Act
            await _fixture.Service.BuildAsync(
                new BuildRequest(
                    [_fixture.Resolver.ResolveTeam(_fixture.Root.Path, "sample")],
                    _fixture.Root.Path,
                    "latest"
                ),
                CancellationToken.None
            );

            // Assert
            var calls = _fixture
                .Runner.ReceivedCalls()
                .Select(call => (ProcessRequest)call.GetArguments()[0]!)
                .ToList();
            var builds = calls.Where(call => call.Arguments.Contains("--load")).ToList();
            var workload = builds.Single(call =>
                call.Arguments.Any(argument => argument.Contains("/components/workload:"))
            );
            if (string.IsNullOrEmpty(token))
            {
                workload.Arguments.Should().NotContain("--secret");
            }
            else
            {
                workload
                    .Arguments.Should()
                    .ContainInOrder("--secret", "id=github_token,env=GITHUB_TOKEN");
                calls.SelectMany(call => call.Arguments).Should().NotContain(token);
            }
            builds
                .Where(call => call != workload)
                .Should()
                .OnlyContain(call => !call.Arguments.Contains("--secret"));
            builds
                .Should()
                .OnlyContain(call =>
                    call.Arguments.Take(6)
                        .SequenceEqual(
                            new[]
                            {
                                "--context",
                                "test-context",
                                "buildx",
                                "build",
                                "--builder",
                                "test-context",
                            }
                        )
                );
            calls
                .Should()
                .NotContain(call =>
                    call.Arguments.Contains("--push") || call.Arguments.Contains("pull")
                );
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previous);
        }
    }
}
