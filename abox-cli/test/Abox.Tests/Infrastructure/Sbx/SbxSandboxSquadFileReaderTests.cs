using Abox.Features.Shared.Credentials;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxSandboxSquadFileReaderTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxSandboxSquadFileReader _reader;

    public SbxSandboxSquadFileReaderTests()
    {
        _reader = new SbxSandboxSquadFileReader(_runner);
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
        var files = await _reader.ReadAsync("sample", CancellationToken.None);

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
        var files = await _reader.ReadAsync("sample", CancellationToken.None);

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
        var act = () => _reader.ReadAsync("sample", CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Contain("sandbox not found");
    }
}
