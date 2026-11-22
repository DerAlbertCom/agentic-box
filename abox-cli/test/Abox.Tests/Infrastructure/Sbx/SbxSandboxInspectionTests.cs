using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxSandboxInspectionTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxSandboxInspection _inspection;

    public SbxSandboxInspectionTests()
    {
        _inspection = new SbxSandboxInspection(new SbxInvoker(_runner));
    }

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
        var found = await _inspection.HasProgramAsync("sample", program, CancellationToken.None);

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

    [Theory]
    [InlineData("/home/agent/workspace\n", "/home/agent/workspace")]
    [InlineData("", null)]
    [InlineData("  \n", null)]
    public async Task TheWorkspaceDirectoryIsWhatTheSandboxReports(string output, string? expected)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, output, ""));

        // Act
        var directory = await _inspection.GetWorkspaceDirectoryAsync(
            "sample",
            CancellationToken.None
        );

        // Assert
        directory.Should().Be(expected);
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(
                        new[] { "exec", "sample", "sh", "-c", "printf %s \"$WORKSPACE_DIR\"" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
