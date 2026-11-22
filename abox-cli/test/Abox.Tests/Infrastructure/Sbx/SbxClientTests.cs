using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxClientTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxClient _client;

    public SbxClientTests()
    {
        _client = new SbxClient(_runner, AboxPaths.For(_root.Path, _root.Path));
    }

    public void Dispose() => _root.Dispose();

    [Theory]
    [InlineData("hc", 0, true)]
    [InlineData("missing", 1, false)]
    [InlineData("missing; echo injected", 1, false)]
    [InlineData("$(echo injected)", 1, false)]
    public async Task ProgramProbesPassTheNameAsDataNotShellCode(
        string program,
        int exitCode,
        bool expected
    )
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, "", ""));

        // Act
        var found = await _client.HasProgramAsync("sample", program, CancellationToken.None);

        // Assert
        found.Should().Be(expected);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(
                        new[] { "exec", "sample", "sh", "-c", "command -v \"$1\"", "_", program }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SquadFilesAreReadInOneExecWithClosedInputAndSplitIntoDescriptorsAndSquadMetadata()
    {
        // Arrange
        static string Encoded(string text) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
        var descriptor = "kind: mixin\n# kit 'ü' squad \nservice: a b\n";
        var squad = """{"Secrets":[{"Service":"jfrog","Prompt":"Größe"}]}""";
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    $"kit {Encoded("jfrog")} {Encoded(descriptor)}\nkit {Encoded("an empty kit")} \r\nsquad {Encoded(squad)}\n",
                    ""
                )
            );

        // Act
        var files = await _client.ReadSquadFilesAsync("sample", CancellationToken.None);

        // Assert
        files
            .Kits.Should()
            .Equal(
                new StagedKitDescriptor("jfrog", descriptor),
                new StagedKitDescriptor("an empty kit", "")
            );
        files.SquadJson.Should().Be(squad);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.Count == 5
                    && request
                        .Arguments.Take(4)
                        .SequenceEqual(new[] { "exec", "sample", "sh", "-c" })
                    && request.Arguments[4].Contains("/usr/share/sandbox/kit/*/kit.yaml")
                    && request.Arguments[4].Contains("/usr/share/abox/squad.json")
                    && request.StandardInput == ""
                    && !request.AttachConsole
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SquadFilesWithoutSquadMetadataHaveNone()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "kit d29ya2xvYWQ= a2luZDogd29ya2xvYWQK\n", ""));

        // Act
        var files = await _client.ReadSquadFilesAsync("sample", CancellationToken.None);

        // Assert
        files.Kits.Should().ContainSingle().Which.Kit.Should().Be("workload");
        files.SquadJson.Should().BeNull();
    }

    [Fact]
    public async Task AFailedSquadFilesExecFails()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, "", "sandbox not found"));

        // Act
        var act = () => _client.ReadSquadFilesAsync("sample", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("sandbox not found");
    }

    [Theory]
    [InlineData("create")]
    [InlineData("remove-sandbox")]
    [InlineData("set-secret")]
    [InlineData("set-custom-secret")]
    [InlineData("remove-secret")]
    [InlineData("add-mcp")]
    public async Task OutcomesPreserveTheExitCodeAndBothStreamsWithoutThrowing(string command)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(17, "stdout", "stderr"));
        var cancellationToken = CancellationToken.None;

        // Act
        var outcome = await (
            command switch
            {
                "create" => _client.CreateAsync(
                    new SbxCreateRequest(
                        "kit",
                        "location",
                        "sample",
                        [],
                        new Dictionary<string, string>(),
                        [],
                        []
                    ),
                    cancellationToken
                ),
                "remove-sandbox" => _client.RemoveSandboxAsync("sample", cancellationToken),
                "set-secret" => _client.SetSecretAsync("service", "value", cancellationToken),
                "set-custom-secret" => _client.SetCustomSecretAsync(
                    new SecretDeclaration("service", Hosts: ["host"], EnvironmentVariable: "ENV"),
                    "value",
                    cancellationToken
                ),
                "remove-secret" => _client.RemoveSecretAsync(
                    new SecretDeclaration("service"),
                    cancellationToken
                ),
                "add-mcp" => _client.AddMcpServerAsync(
                    "server",
                    "https://example.com",
                    cancellationToken
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            }
        );

        // Assert
        outcome.ExitCode.Should().Be(17);
        outcome.Output.Should().Be("stdoutstderr");
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
        var outcome = await _client.CreateAsync(
            new SbxCreateRequest(
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
        outcome.Output.Should().Be("Building kit 'git'\nWARN: slow mirror\ntoken is [REDACTED]\n");
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
        PullPolicy.Always
    )]
    [InlineData("/kits/workload", PullPolicy.Never)]
    public async Task CreationPassesTheExplicitPullPolicy(string source, PullPolicy pullPolicy)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        // Act
        var outcome = await _client.CreateAsync(
            new SbxCreateRequest(
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
        outcome.Succeeded.Should().BeTrue();
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[]
                        {
                            "create",
                            source,
                            "/workspace",
                            "--name",
                            "sample",
                            "--pull",
                            pullPolicy == PullPolicy.Always ? "always" : "never",
                            "--no-input",
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CreationNeverLetsSbxWaitForAnAnswer()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        // Act
        await _client.CreateAsync(
            new SbxCreateRequest(
                "/kits/workload",
                "/workspace",
                "sample",
                ["/kits/github-auth"],
                new Dictionary<string, string>(),
                [],
                []
            ),
            CancellationToken.None
        );

        // Assert
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments[0] == "create" && request.Arguments.Contains("--no-input")
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
        await _client.CreateAsync(
            new SbxCreateRequest(
                "/runtime/workload",
                "/workspace",
                "sample",
                ["/kits/config"],
                new Dictionary<string, string> { ["mode"] = "team" },
                [],
                ["elastic"],
                HostDockerContext: "desktop-linux"
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
                    && request.Arguments.Contains("mode=team")
                    && request.Arguments.Contains("/kits/config")
                    && request.Arguments.Contains("elastic")
                ),
                Arg.Any<CancellationToken>()
            );
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
                        "detached": true
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
        var sandboxes = await _client.ListSandboxesAsync(CancellationToken.None);

        // Assert
        sandboxes.Should().HaveCount(2);
        sandboxes[0].Name.Should().Be("agentic-box-abox-12345");
        sandboxes[0].IsRunning.Should().BeTrue();
        sandboxes[0].CreatedAt.Should().Be(DateTimeOffset.Parse("2026-11-08T14:27:10Z"));
        sandboxes[1].IsRunning.Should().BeFalse();
        sandboxes[1].CreatedAt.Should().BeNull();
    }

    [Fact]
    public async Task TheMcpServersAreReadFromTheJsonOutput()
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
                        "name": "elastic",
                        "transport": "remote http",
                        "status": "ready",
                        "type": "remote",
                        "gateway": { "name": "LOCAL", "local": true }
                      }
                    ]
                    """,
                    string.Empty
                )
            );

        // Act
        var servers = await _client.ListMcpServersAsync(CancellationToken.None);

        // Assert
        servers.Should().Equal("elastic");
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(new[] { "mcp", "ls", "--json" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheSecretsAreReadFromTheJsonOutput()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    """
                    {
                      "secrets": [{ "name": "github" }],
                      "custom_secrets": [
                        { "targets": ["api.example.com"], "env": "EXAMPLE_TOKEN" }
                      ],
                      "shadowed_services": [],
                      "env_only_count": 0
                    }
                    """,
                    string.Empty
                )
            );

        // Act
        var secrets = await _client.ListSecretsAsync(CancellationToken.None);

        // Assert
        secrets.Services.Should().Equal("github");
        var custom = secrets.Custom.Should().ContainSingle().Which;
        custom.Targets.Should().Equal("api.example.com");
        custom.EnvironmentVariable.Should().Be("EXAMPLE_TOKEN");
    }

    [Fact]
    public async Task OutputThatIsNotJsonIsAnErrorThatNamesTheCommand()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "not json", string.Empty));

        // Act
        var action = () => _client.ListSandboxesAsync(CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("sbx ls");
    }

    [Fact]
    public async Task AFailedCommandIsAnErrorWithWhatSbxWrote()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "boom"));

        // Act
        var action = () => _client.ListSandboxesAsync(CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("boom");
    }
}
