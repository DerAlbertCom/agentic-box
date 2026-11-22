using Abox.Features.Shared.Common;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxSandboxLifecycleTests
{
    private const string FailedKitOutput = """
        Creating sandbox...
          ✓ apt-get install -y git (kit=git, user=root, 2.1s)
          ✗ /opt/kits/rwe-ghec/install.sh --check (kit=rwe-ghec, user=agent, exit 1)
            ── captured output ──
            RWE_GHE_TOKEN is not bound.
            Configure the four-host RWE_GHE_TOKEN binding; see teams/mti-ca/README.md.
            ─────────────────────
          ✗ /opt/kits/other/install.sh (kit=other-kit, user=agent, exit 2)
            ── captured output ──
            other failure
            ─────────────────────
        WARN: kit 'rwe-ghec' left the sandbox incomplete
        error: failed to apply kit to sandbox

        """;

    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxSandboxLifecycle _lifecycle;

    public SbxSandboxLifecycleTests()
    {
        _lifecycle = new SbxSandboxLifecycle(new SbxInvoker(_runner));
    }

    [Fact]
    public async Task TheSandboxesAreReadFromTheJsonOutput()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    [
                      {
                        "name": "agentic-box-abox-12345",
                        "id": "1749681b-5622-4313-9d80-bf971233fcea",
                        "agent": "shell",
                        "status": "running",
                        "last_used_at": "2026-11-08T15:03:06.304065176Z",
                        "cpus": 4,
                        "memory": "8192m",
                        "memory_mib": 8192,
                        "created_at": "2026-11-08T14:27:10Z",
                        "detached": true,
                        "workspaces": ["/work/abox", "/work/other"]
                      },
                      {
                        "name": "stopped-one",
                        "status": "stopped"
                      }
                    ]
                    """,
                    string.Empty
                )
            );

        // Act
        var sandboxes = await _lifecycle.ListAsync(CancellationToken.None);

        // Assert
        sandboxes.Should().HaveCount(2);
        sandboxes[0].Name.Should().Be("agentic-box-abox-12345");
        sandboxes[0].IsRunning.Should().BeTrue();
        sandboxes[0].CreatedAt.Should().Be(DateTimeOffset.Parse("2026-11-08T14:27:10Z"));
        sandboxes[0].Workspaces.Should().Equal("/work/abox", "/work/other");
        sandboxes[1].IsRunning.Should().BeFalse();
        sandboxes[1].CreatedAt.Should().BeNull();
        sandboxes[1].Workspaces.Should().BeEmpty();
    }

    [Fact]
    public async Task OutputThatIsNotJsonIsAnErrorThatNamesTheCommand()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "not json", string.Empty));

        // Act
        var action = () => _lifecycle.ListAsync(CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("sbx ls");
    }

    [Fact]
    public async Task AFailedListingIsAnErrorWithWhatSbxWrote()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "boom"));

        // Act
        var action = () => _lifecycle.ListAsync(CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("boom");
    }

    [Fact]
    public async Task ARefusedCreationKeepsBothStreamsWithoutThrowing()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(17, "stdout", "stderr"));

        // Act
        var creation = await _lifecycle.CreateAsync(Definition(), CancellationToken.None);

        // Assert
        creation.Succeeded.Should().BeFalse();
        creation.Output.Should().Be("stdoutstderr");
    }

    [Fact]
    public async Task ASuccessfulCreationHasNoFailure()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "Note: done\n", string.Empty));

        // Act
        var creation = await _lifecycle.CreateAsync(Definition(), CancellationToken.None);

        // Assert
        creation.Succeeded.Should().BeTrue();
        creation.Failure.Should().BeNull();
        creation.Output.Should().Be("Note: done\n");
    }

    [Fact]
    public async Task AFailedKitStepLeadsTheFailureWithItsKitAndCapturedOutput()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, FailedKitOutput, string.Empty));

        // Act
        var creation = await _lifecycle.CreateAsync(Definition(), CancellationToken.None);

        // Assert
        creation
            .Failure.Should()
            .Be(
                string.Join(
                    Environment.NewLine,
                    "The kit 'rwe-ghec' failed (exit 1): /opt/kits/rwe-ghec/install.sh --check",
                    "    RWE_GHE_TOKEN is not bound.",
                    "    Configure the four-host RWE_GHE_TOKEN binding; see teams/mti-ca/README.md."
                )
            );
        creation.Output.Should().Be(FailedKitOutput);
    }

    [Fact]
    public async Task UnrecognizedFailureOutputFallsBackToTheLastErrorLine()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    1,
                    "Pulling image...\nerror: first problem\nmore detail\nerror: network policy denied\n",
                    string.Empty
                )
            );

        // Act
        var creation = await _lifecycle.CreateAsync(Definition(), CancellationToken.None);

        // Assert
        creation.Failure.Should().Be("error: network policy denied");
    }

    [Fact]
    public async Task ASilentFailureNamesTheExitCode()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(3, string.Empty, string.Empty));

        // Act
        var creation = await _lifecycle.CreateAsync(Definition(), CancellationToken.None);

        // Assert
        creation.Failure.Should().Be("sbx exited with code 3.");
    }

    [Fact]
    public async Task CreationStreamsTheProgressInArrivalOrderWithKnownSecretsRedacted()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var lines = call.Arg<ProcessRequest>().OutputLineSink!;
                lines(new ProcessOutputLine(ProcessStream.StandardOutput, "Building kit 'git'"));
                lines(new ProcessOutputLine(ProcessStream.StandardError, "WARN: slow mirror"));
                lines(new ProcessOutputLine(ProcessStream.StandardOutput, "token is s3cr3t-value"));
                return new ProcessResult(
                    1,
                    "Building kit 'git'\ntoken is s3cr3t-value\n",
                    "WARN: slow mirror\n"
                )
                {
                    Output = "Building kit 'git'\nWARN: slow mirror\ntoken is s3cr3t-value\n",
                };
            });
        var progress = new List<string>();

        // Act
        var creation = await _lifecycle.CreateAsync(
            new SandboxDefinition(
                "kit",
                "/workspace",
                "sample",
                [],
                new Dictionary<string, string> { ["api_token"] = "s3cr3t-value" },
                [],
                [],
                Progress: progress.Add
            ),
            CancellationToken.None
        );

        // Assert
        progress.Should().Equal("Building kit 'git'", "WARN: slow mirror", "token is [REDACTED]");
        creation.Output.Should().Be("Building kit 'git'\nWARN: slow mirror\ntoken is [REDACTED]\n");
        creation.Failure.Should().Be("token is [REDACTED]");
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--no-input")),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(
        "rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-sample/ac:latest",
        PullPolicy.Always,
        "always"
    )]
    [InlineData("/kits/workload", PullPolicy.Never, "never")]
    public async Task CreationPassesTheExplicitPullPolicy(
        string source,
        PullPolicy pullPolicy,
        string pullArgument
    )
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        // Act
        var creation = await _lifecycle.CreateAsync(
            new SandboxDefinition(
                source,
                "/workspace",
                "sample",
                [],
                new Dictionary<string, string>(),
                [],
                [],
                pullPolicy
            ),
            CancellationToken.None
        );

        // Assert
        creation.Succeeded.Should().BeTrue();
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(
                        new[]
                        {
                            "create",
                            source,
                            "/workspace",
                            "--name",
                            "sample",
                            "--pull",
                            pullArgument,
                            "--no-input",
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MixinsKitArgumentsMountsAndStaticMcpServersBecomeCreateFlags()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        // Act
        await _lifecycle.CreateAsync(
            new SandboxDefinition(
                "/kits/workload",
                "/workspace",
                "sample",
                ["/kits/github-auth", "/kits/state"],
                new Dictionary<string, string> { ["mode"] = "team" },
                [new SandboxMount("/state/sample/.config", "/home/agent/.config")],
                ["elastic", "jira"]
            ),
            CancellationToken.None
        );

        // Assert
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Environment == null
                    && request
                        .Arguments.Skip(8)
                        .SequenceEqual(
                            new[]
                            {
                                "--kit",
                                "/kits/github-auth",
                                "--kit",
                                "/kits/state",
                                "--kit-arg",
                                "mode=team",
                                "--mount",
                                "/state/sample/.config:/home/agent/.config",
                                "--static-mcp",
                                "elastic,jira",
                            }
                        )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ThinLocalKitsUseTheHostBuilderAndRecordedDockerContext()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));

        // Act
        await _lifecycle.CreateAsync(
            new SandboxDefinition(
                "/runtime/workload",
                "/workspace",
                "sample",
                ["/kits/config"],
                new Dictionary<string, string> { ["mode"] = "team" },
                [],
                ["elastic"],
                HostImageStore: "desktop-linux"
            ),
            CancellationToken.None
        );

        // Assert
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Environment!["SBX_KIT_BUILDER"] == "host"
                    && request.Environment["BUILDX_BUILDER"] == "desktop-linux"
                    && request.Environment["DOCKER_CONTEXT"] == "desktop-linux"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(17, false)]
    public async Task RemovalForcesAndKeepsWhatSbxWrote(int exitCode, bool removed)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, "stdout", "stderr"));

        // Act
        var removal = await _lifecycle.RemoveAsync("sample", CancellationToken.None);

        // Assert
        removal.Should().Be(new SandboxRemoval(removed, "stdoutstderr"));
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(new[] { "rm", "sample", "--force" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    private static SandboxDefinition Definition() =>
        new("kit", "location", "sample", [], new Dictionary<string, string>(), [], []);
}
