using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Registry;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class DockerRegistryPublisherTests : IDisposable
{
    private const string Reference = "registry.example/team/kit:latest";
    private static readonly string Digest = "sha256:" + new string('b', 64);
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly TempDirectory _work = new();

    public void Dispose() => _work.Dispose();

    private DockerRegistryPublisher Publisher() => new(Substitute.For<IHostPrograms>(), _runner);

    public static TheoryData<string, bool, string[]> FailedOperations =>
        new()
        {
            { "probe", false, ["buildx", "version"] },
            {
                "tag",
                true,
                [
                    "buildx",
                    "imagetools",
                    "--builder",
                    "publisher",
                    "create",
                    "--prefer-index=false",
                    "--tag",
                    Reference,
                    "candidate",
                ]
            },
            {
                "inspect",
                false,
                ["buildx", "imagetools", "--builder", "publisher", "inspect", "--raw", Reference]
            },
        };

    [Theory]
    [MemberData(nameof(FailedOperations))]
    public async Task FailedDockerInvocationsReportThePublishingFailureWithCapturedOutput(
        string operation,
        bool attachConsole,
        string[] expected
    )
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(17, "output", "error"));
        var publisher = Publisher();

        // Act
        Func<Task> action = operation switch
        {
            "tag" => () =>
                publisher.TagAsync("candidate", Reference, "publisher", CancellationToken.None),
            "inspect" => () =>
                publisher.InspectAsync(Reference, "publisher", CancellationToken.None),
            _ => () => publisher.RequireReadyAsync(CancellationToken.None),
        };

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .StartWith("Publishing failed (docker buildx, exit code 17).")
            .And.EndWith(Environment.NewLine + "erroroutput");
        await _runner
            .Received()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.AttachConsole == attachConsole
                    && request.Arguments.SequenceEqual(expected)
                ),
                CancellationToken.None
            );
    }

    [Fact]
    public async Task PushBuildsWithTheBuilderAndReturnsTheDigestFromTheBuildMetadata()
    {
        // Arrange
        ProcessRequest? push = null;
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                push = call.Arg<ProcessRequest>();
                var metadata = push.Arguments[
                    push.Arguments.ToList().IndexOf("--metadata-file") + 1
                ];
                File.WriteAllText(metadata, $"{{\"containerimage.digest\":\"{Digest}\"}}");
                return new ProcessResult(0, "", "");
            });

        // Act
        var digest = await Publisher()
            .PushAsync(
                new RegistryPush(
                    "source",
                    "source/workload.yaml",
                    Reference,
                    _work.Path,
                    "publisher",
                    false
                ),
                CancellationToken.None
            );

        // Assert
        digest.Should().Be(Digest);
        push!.AttachConsole.Should().BeTrue();
        push.Arguments.Should()
            .StartWith([
                "buildx",
                "build",
                "--builder",
                "publisher",
                "--file",
                "source/workload.yaml",
                "--tag",
                Reference,
                "--metadata-file",
            ])
            .And.EndWith(["--provenance=mode=min", "--push", "source"]);
        Path.GetDirectoryName(push.Arguments[9]).Should().Be(_work.Path);
    }

    [Fact]
    public async Task APushWithoutReadableMetadataHasNoDigest()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));

        // Act
        var digest = await Publisher()
            .PushAsync(
                new RegistryPush(
                    "source",
                    "source/workload.yaml",
                    Reference,
                    _work.Path,
                    null,
                    false
                ),
                CancellationToken.None
            );

        // Assert
        digest.Should().BeNull();
    }

    [Fact]
    public async Task InspectionReadsAnIndexAsPlatformImagesAndMarksAttestations()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(
                    0,
                    $$$"""
                    {"schemaVersion":2,"manifests":[
                      {"digest":"{{{Digest}}}","platform":{"os":"linux","architecture":"arm64"}},
                      {"digest":"invalid","platform":{"os":"unknown","architecture":"unknown"},
                       "annotations":{"vnd.docker.reference.type":"attestation-manifest"}}]}
                    """,
                    ""
                )
            );

        // Act
        var manifest = await Publisher().InspectAsync(Reference, null, CancellationToken.None);

        // Assert
        manifest.KitDescriptor.Should().BeNull();
        manifest
            .Platforms.Should()
            .Equal(
                new PlatformManifest(Digest, "linux", "arm64"),
                new PlatformManifest("invalid", "unknown", "unknown", IsAttestation: true)
            );
    }
}
