using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxSandboxExecTests
{
    [Theory]
    [InlineData(SandboxExecInput.None, false, false)]
    [InlineData(SandboxExecInput.Forwarded, true, false)]
    [InlineData(SandboxExecInput.Terminal, true, true)]
    public async Task TheBootstrapPreservesArgvStreamsWorkdirAndExitCode(
        SandboxExecInput input,
        bool keepsInputOpen,
        bool allocatesTty
    )
    {
        // Arrange
        var (runner, observed) = Runner(37);
        var exec = new SbxSandboxExec(runner);
        string[] command = ["sh", "-c", "printf '%s' \"$1\"", "_", "a b;$(literal)\n\"'🔒"];

        // Act
        var outcome = await exec.RunAsync(
            new SandboxExecRequest("actual", command, input, WorkingDirectory: "/workspace/a b"),
            CancellationToken.None
        );

        // Assert
        outcome.ExitCode.Should().Be(37);
        outcome.Succeeded.Should().BeFalse();
        var request = observed.Single();
        request.FileName.Should().Be("sbx");
        request.AttachConsole.Should().BeTrue();
        request.StandardInput.Should().BeNull();
        request.Arguments.Contains("-i").Should().Be(keepsInputOpen);
        request.Arguments.Contains("-t").Should().Be(allocatesTty);
        request.Arguments.Should().Contain("/workspace/a b");
        request.Arguments.Sum(argument => argument.Length).Should().BeLessThan(4000);
        request.Arguments.TakeLast(command.Length).Should().Equal(command);
        request.Environment.Should().BeNull();
    }

    [Fact]
    public async Task EveryCommandCrossesTheProtectedBootstrapInTheSandbox()
    {
        // Arrange
        var (runner, observed) = Runner(0);
        var exec = new SbxSandboxExec(runner);

        // Act
        var outcome = await exec.RunAsync(
            new SandboxExecRequest("actual", ["true"]),
            CancellationToken.None
        );

        // Assert
        outcome.Succeeded.Should().BeTrue();
        var request = observed.Single();
        request.Arguments.Should().StartWith(["exec", "actual", "sh", "-c"]);
        request.Arguments.Should().Contain("abox-https");
        request.Arguments[^1].Should().Be("true");
    }

    [Fact]
    public async Task TheEnvironmentIsSetForTheCommandInTheSandbox()
    {
        // Arrange
        var (runner, observed) = Runner(0);
        var exec = new SbxSandboxExec(runner);

        // Act
        await exec.RunAsync(
            new SandboxExecRequest(
                "actual",
                ["true"],
                Environment: new Dictionary<string, string> { ["SBOX_TERMINAL_THEME"] = "1" }
            ),
            CancellationToken.None
        );

        // Assert
        observed
            .Single()
            .Arguments.Should()
            .StartWith(["exec", "-e", "SBOX_TERMINAL_THEME=1", "actual"]);
    }

    private static (IProcessRunner Runner, List<ProcessRequest> Observed) Runner(int exitCode)
    {
        var runner = Substitute.For<IProcessRunner>();
        var observed = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed.Add(call.Arg<ProcessRequest>());
                return new ProcessResult(exitCode, "", "");
            });
        return (runner, observed);
    }
}
