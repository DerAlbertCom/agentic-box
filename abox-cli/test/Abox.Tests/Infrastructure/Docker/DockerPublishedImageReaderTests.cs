using System.Text.Json;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class DockerPublishedImageReaderTests
{
    private const string Reference = "registry.example/team/set:latest";
    private static readonly string Digest = "sha256:" + new string('c', 64);
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();

    private DockerPublishedImageReader Reader() => new(Substitute.For<IHostPrograms>(), _runner);

    private void Respond(string output) =>
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, output, ""));

    private Task Ran(params string[] arguments) =>
        _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && !request.AttachConsole
                    && request.Arguments.SequenceEqual(arguments)
                ),
                CancellationToken.None
            );

    [Fact]
    public async Task ResolvingATagReadsTheDigestOfItsManifestIdentity()
    {
        // Arrange
        Respond($"{{\"digest\":\"{Digest}\"}}");

        // Act
        var digest = await Reader().ResolveDigestAsync(Reference, CancellationToken.None);

        // Assert
        digest.Should().Be(Digest);
        await Ran("buildx", "imagetools", "inspect", "--format", "{{json .Manifest}}", Reference);
    }

    [Fact]
    public async Task InspectionReturnsTheRetainedKitDescriptor()
    {
        // Arrange
        Respond(
            JsonSerializer.Serialize(
                new
                {
                    schemaVersion = 2,
                    annotations = new Dictionary<string, string>
                    {
                        [DockerCommand.DescriptorAnnotation] =
                            "{\"schemaVersion\":\"3\",\"kind\":\"mixin\"}",
                    },
                }
            )
        );

        // Act
        var manifest = await Reader().InspectAsync(Reference, CancellationToken.None);

        // Assert
        manifest.Platforms.Should().BeNull();
        manifest
            .Descriptor("mixin", "unexpected")
            .GetProperty("kind")
            .GetString()
            .Should()
            .Be("mixin");
        await Ran("buildx", "imagetools", "inspect", "--raw", Reference);
    }

    [Fact]
    public async Task SavingPullsTheHostPlatformThenWritesTheArchive()
    {
        // Arrange
        Respond("");

        // Act
        await Reader().SaveAsync(Reference, "linux/amd64", "config.tar", CancellationToken.None);

        // Assert
        Received.InOrder(() =>
        {
            _runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[] { "pull", "--platform", "linux/amd64", Reference }
                    )
                ),
                CancellationToken.None
            );
            _runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[] { "image", "save", "--output", "config.tar", Reference }
                    )
                ),
                CancellationToken.None
            );
        });
    }

    [Theory]
    [InlineData("pull")]
    [InlineData("buildx")]
    public async Task FailuresNeverEchoDockerOutput(string command)
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(9, "token-output", "token-error"));
        var reader = Reader();

        // Act
        Func<Task> action =
            command == "pull"
                ? () =>
                    reader.SaveAsync(Reference, "linux/amd64", "config.tar", CancellationToken.None)
                : () => reader.InspectAsync(Reference, CancellationToken.None);

        // Assert
        var message = (await action.Should().ThrowAsync<AboxException>()).Which.Message;
        message
            .Should()
            .StartWith(
                $"Cannot read the published squad metadata (docker {command}, exit code 9)."
            );
        message.Should().NotContain("token");
    }
}
