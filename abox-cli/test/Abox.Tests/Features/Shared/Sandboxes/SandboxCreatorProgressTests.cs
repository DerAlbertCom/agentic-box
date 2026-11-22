using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorProgressTests : IDisposable
{
    private const string FailedKitOutput = """
        Creating sandbox...
          ✗ /opt/kits/rwe-ghec/install.sh --check (kit=rwe-ghec, user=agent, exit 1)
        WARN: kit 'rwe-ghec' left the sandbox incomplete
        error: failed to apply kit to sandbox

        """;

    private const string Failure = """
        The kit 'rwe-ghec' failed (exit 1): /opt/kits/rwe-ghec/install.sh --check
            RWE_GHE_TOKEN is not bound.
        """;

    private readonly SandboxCreatorFixture _fixture = new();

    public void Dispose() => _fixture.Root.Dispose();

    [Fact]
    public async Task TheFailureLeadsTheErrorAndTheFullOutputFollows()
    {
        // Arrange
        _fixture.SaveBuild();
        FailCreate(FailedKitOutput);

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        var lines = message.Split(Environment.NewLine);
        lines[0]
            .Should()
            .Be($"sbx could not create the sandbox '{SandboxName.For(_fixture.Root.Path)}'.");
        message.Should().Contain(Environment.NewLine + Failure + Environment.NewLine);
        message[message.IndexOf("Full sbx output:", StringComparison.Ordinal)..]
            .Should()
            .Contain("WARN: kit 'rwe-ghec' left the sandbox incomplete")
            .And.Contain("error: failed to apply kit to sandbox");
    }

    [Fact]
    public async Task AFailedReplacementSaysTheOldSandboxIsGoneAndHowToRecreateIt()
    {
        // Arrange
        _fixture.SaveBuild();
        var name = SandboxName.For(_fixture.Root.Path);
        _fixture
            .Lifecycle.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        FailCreate(FailedKitOutput);

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        message
            .Should()
            .Contain($"The old sandbox '{name}' was removed")
            .And.Contain("state directories on the host are kept")
            .And.Contain(
                $"abox create --team sample --squad ac --tag latest --local --location \"{_fixture.Root.Path}\""
            );
    }

    [Fact]
    public async Task AFailedFirstCreateSaysHowToRetryWithoutMentioningARemoval()
    {
        // Arrange
        _fixture.SaveBuild();
        FailCreate(FailedKitOutput);

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        message
            .Should()
            .NotContain("removed")
            .And.Contain(
                $"Retry with: abox create --team sample --squad ac --tag latest --local --location \"{_fixture.Root.Path}\""
            );
    }

    [Fact]
    public async Task ProgressReachesTheConsoleInOrderWhileSbxRuns()
    {
        // Arrange
        _fixture.SaveBuild();
        var seenDuringCreate = new List<string>();
        _fixture
            .Lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.Arg<SandboxDefinition>().Progress!;
                progress("Building kit 'git'");
                progress("WARN: slow mirror");
                progress("Note: no binding authorizes elastic.");
                seenDuringCreate.AddRange(_fixture.Console.Output);
                return new SandboxCreation(
                    "Building kit 'git'\nWARN: slow mirror\nNote: no binding authorizes elastic.\n"
                );
            });

        // Act
        var exitCode = await _fixture.Service.CreateAsync(
            _fixture.Request(),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        seenDuringCreate
            .Should()
            .ContainInOrder(
                "  Building kit 'git'",
                "  WARN: slow mirror",
                "  Note: no binding authorizes elastic."
            );
        _fixture
            .Console.Output.Should()
            .ContainInOrder(
                "  Note: no binding authorizes elastic.",
                "Note: no binding authorizes elastic."
            );
    }

    [Fact]
    public async Task AStreamedFailureIsNotDumpedAgain()
    {
        // Arrange
        _fixture.SaveBuild();
        _fixture
            .Lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.Arg<SandboxDefinition>().Progress!;
                foreach (var line in FailedKitOutput.Split('\n'))
                    progress(line);
                return new SandboxCreation(FailedKitOutput, Failure);
            });

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        message
            .Should()
            .Contain(Failure)
            .And.Contain("The full sbx output is shown above.")
            .And.NotContain("Full sbx output:")
            .And.NotContain("error: failed to apply kit");
        _fixture.Console.Output.Should().Contain("  error: failed to apply kit to sandbox");
    }

    private void FailCreate(string output) =>
        _fixture
            .Lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(output, Failure));
}
