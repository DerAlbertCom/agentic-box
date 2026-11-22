using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxHostSecretStoreTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxHostSecretStore _store;

    public SbxHostSecretStoreTests()
    {
        _store = new SbxHostSecretStore(_runner);
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
        var secrets = await _store.ListAsync(CancellationToken.None);

        // Assert
        secrets.Services.Should().Equal("github");
        var custom = secrets.Custom.Should().ContainSingle().Which;
        custom.Targets.Should().Equal("api.example.com");
        custom.EnvironmentVariable.Should().Be("EXAMPLE_TOKEN");
    }

    [Theory]
    [InlineData("set")]
    [InlineData("set-custom")]
    [InlineData("remove")]
    public async Task AFailedChangeSaysWhatSbxWroteWithoutThrowing(string change)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(17, "stdout\n", "stderr\n"));
        var cancellationToken = CancellationToken.None;

        // Act
        var result = await (
            change switch
            {
                "set" => _store.SetAsync("service", "value", cancellationToken),
                "set-custom" => _store.SetCustomAsync(
                    new SecretDeclaration("service", Hosts: ["host"], EnvironmentVariable: "ENV"),
                    "value",
                    cancellationToken
                ),
                "remove" => _store.RemoveAsync(
                    new SecretIdentity(SecretIdentityKind.Service, "service"),
                    cancellationToken
                ),
                _ => throw new ArgumentOutOfRangeException(nameof(change)),
            }
        );

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("stdout\nstderr");
    }

    [Theory]
    [InlineData(
        SecretIdentityKind.Service,
        "github",
        new[] { "secret", "rm", "github", "--force" }
    )]
    [InlineData(
        SecretIdentityKind.Host,
        "api.example.com",
        new[] { "secret", "rm", "--host", "api.example.com", "--force" }
    )]
    [InlineData(
        SecretIdentityKind.EnvironmentVariable,
        "EXAMPLE_TOKEN",
        new[] { "secret", "rm", "--env-name", "EXAMPLE_TOKEN", "--force" }
    )]
    public async Task ASecretIsRemovedByItsIdentityWithoutAsking(
        SecretIdentityKind kind,
        string value,
        string[] arguments
    )
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));

        // Act
        var result = await _store.RemoveAsync(
            new SecretIdentity(kind, value),
            CancellationToken.None
        );

        // Assert
        result.Should().Be(HostSecretChange.Done);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx" && request.Arguments.SequenceEqual(arguments)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ACustomSecretWithoutAValueIsTakenFromItsHostCommandWithoutInput()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        var declaration = new SecretDeclaration(
            "rwe-ghec",
            Hosts: ["rwe.ghe.com"],
            EnvironmentVariable: "RWE_GHE_TOKEN",
            Command: new HostCommand("gh auth token -h rwe.ghe.com"),
            Placeholder: "gho_{rand}"
        );

        // Act
        var result = await _store.SetCustomAsync(declaration, null, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(
                        new[]
                        {
                            "secret",
                            "set-custom",
                            "--host",
                            "rwe.ghe.com",
                            "--env",
                            "RWE_GHE_TOKEN",
                            "--placeholder",
                            "gho_{rand}",
                            "--command",
                            "gh auth token -h rwe.ghe.com",
                        }
                    )
                    && request.StandardInput == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public void TheManualInstructionIsTheSbxCommandLineQuotedForAPosixShell()
    {
        // Arrange
        var declaration = new SecretDeclaration(
            "rwe-ghec",
            Hosts: ["copilot-api.rwe.ghe.com", "api.rwe.ghe.com"],
            EnvironmentVariable: "RWE_GHE_TOKEN",
            Command: HostCommand.Withheld,
            Placeholder: "gho_{rand}"
        );

        // Act
        var instruction = _store.ManualCustomSecretInstruction(declaration, "<host command>");

        // Assert
        instruction
            .Should()
            .Be(
                "sbx secret set-custom --host copilot-api.rwe.ghe.com --host api.rwe.ghe.com --env RWE_GHE_TOKEN --placeholder 'gho_{rand}' --command '<host command>'"
            );
        _runner.ReceivedCalls().Should().BeEmpty();
    }
}
