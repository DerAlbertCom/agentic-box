using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxMcpServerRegistryTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxMcpServerRegistry _registry;

    public SbxMcpServerRegistryTests()
    {
        _registry = new SbxMcpServerRegistry(new SbxInvoker(_runner));
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
                      },
                      { "transport": "stdio" }
                    ]
                    """,
                    string.Empty
                )
            );

        // Act
        var servers = await _registry.ListAsync(CancellationToken.None);

        // Assert
        servers.Should().Equal("elastic");
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(new[] { "mcp", "ls", "--json" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(17, false)]
    public async Task RegistrationPassesTheUrlAndKeepsWhatSbxWrote(int exitCode, bool registered)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(exitCode, "stdout", "stderr"));

        // Act
        var registration = await _registry.AddAsync(
            "server",
            "https://example.com",
            CancellationToken.None
        );

        // Assert
        registration.Should().Be(new McpServerRegistration(registered, "stdoutstderr"));
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx"
                    && request.Arguments.SequenceEqual(
                        new[] { "mcp", "add", "server", "--url", "https://example.com" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
