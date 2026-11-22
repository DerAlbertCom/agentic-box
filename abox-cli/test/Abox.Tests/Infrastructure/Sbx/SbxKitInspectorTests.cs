using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxKitInspectorTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxKitInspector _inspector;

    public SbxKitInspectorTests()
    {
        _inspector = new SbxKitInspector(_runner);
    }

    [Fact]
    public async Task TheKitIsInspectedWithItsArguments()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty));

        // Act
        var inspection = await _inspector.InspectAsync(
            "/kits/sample",
            new Dictionary<string, string> { ["version"] = "1.2", ["flavor"] = "slim" },
            CancellationToken.None
        );

        // Assert
        inspection.Succeeded.Should().BeTrue();
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(
                        new[]
                        {
                            "kit",
                            "inspect",
                            "/kits/sample",
                            "--kit-arg",
                            "version=1.2",
                            "--kit-arg",
                            "flavor=slim",
                        }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ARejectedKitReportsWhatSbxSaid()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, "checking\n", "unknown field 'foo'\n"));

        // Act
        var inspection = await _inspector.InspectAsync(
            "/kits/sample",
            new Dictionary<string, string>(),
            CancellationToken.None
        );

        // Assert
        inspection.Succeeded.Should().BeFalse();
        inspection.Output.Should().Be("checking\nunknown field 'foo'\n");
    }
}
