using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorProgressTests : IDisposable
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

    private readonly SandboxCreatorFixture _fixture = new();

    public void Dispose() => _fixture.Root.Dispose();

    [Fact]
    public async Task AFailedKitStepLeadsTheErrorWithItsKitAndCapturedOutput()
    {
        // Arrange
        _fixture.SaveBuild();
        FailCreate(FailedKitOutput);

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        var headline = message[..message.IndexOf("Full sbx output:", StringComparison.Ordinal)];
        headline
            .Should()
            .StartWith($"sbx could not create the sandbox '{SandboxName.For(_fixture.Root.Path)}'.")
            .And.Contain(
                "The kit 'rwe-ghec' failed (exit 1): /opt/kits/rwe-ghec/install.sh --check"
            )
            .And.Contain("    RWE_GHE_TOKEN is not bound.")
            .And.Contain(
                "    Configure the four-host RWE_GHE_TOKEN binding; see teams/mti-ca/README.md."
            )
            .And.NotContain("other-kit")
            .And.NotContain("error: failed to apply kit");
        message
            .Should()
            .Contain("Full sbx output:")
            .And.Contain("WARN: kit 'rwe-ghec' left the sandbox incomplete");
    }

    [Fact]
    public async Task UnrecognizedFailureOutputFallsBackToTheLastErrorLine()
    {
        // Arrange
        _fixture.SaveBuild();
        FailCreate(
            "Pulling image...\nerror: first problem\nmore detail\nerror: network policy denied\n"
        );

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        message.Split(Environment.NewLine)[1].Should().Be("error: network policy denied");
    }

    [Fact]
    public async Task AFailedReplacementSaysTheOldSandboxIsGoneAndHowToRecreateIt()
    {
        // Arrange
        _fixture.SaveBuild();
        var name = SandboxName.For(_fixture.Root.Path);
        _fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
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
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.Arg<SbxCreateRequest>().Progress!;
                progress("Building kit 'git'");
                progress("WARN: slow mirror");
                progress("Note: no binding authorizes elastic.");
                seenDuringCreate.AddRange(_fixture.Console.Output);
                return new SbxOutcome(
                    0,
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
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var progress = call.Arg<SbxCreateRequest>().Progress!;
                foreach (var line in FailedKitOutput.Split('\n'))
                    progress(line);
                return new SbxOutcome(1, FailedKitOutput);
            });

        // Act
        var action = () => _fixture.Service.CreateAsync(_fixture.Request(), CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        message
            .Should()
            .Contain("The kit 'rwe-ghec' failed (exit 1)")
            .And.Contain("The full sbx output is shown above.")
            .And.NotContain("Full sbx output:")
            .And.NotContain("error: failed to apply kit");
        _fixture.Console.Output.Should().Contain("  error: failed to apply kit to sandbox");
    }

    private void FailCreate(string output) =>
        _fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, output));
}
