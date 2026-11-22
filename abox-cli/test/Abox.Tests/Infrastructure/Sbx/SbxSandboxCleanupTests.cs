using Abox.Features.Shared.SandboxCleanup;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SbxSandboxCleanupTests
{
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly SbxSandboxCleanup _cleanup;

    public SbxSandboxCleanupTests()
    {
        _cleanup = new SbxSandboxCleanup(new SbxInvoker(_runner));
    }

    [Fact]
    public async Task TheSandboxesAreReadFromTheJsonOutput()
    {
        // Arrange
        Answer(
            ["ls", "--json"],
            new ProcessResult(
                0,
                """
                [
                  { "name": "agentic-box-abox-12345", "status": "running", "created_at": "2026-11-08T14:27:10Z" },
                  { "name": "stopped-one", "status": "stopped" },
                  { "status": "running" }
                ]
                """,
                string.Empty
            )
        );

        // Act
        var sandboxes = await _cleanup.ListSandboxesAsync(CancellationToken.None);

        // Assert
        sandboxes
            .Should()
            .Equal(
                new SandboxSummary("agentic-box-abox-12345", true),
                new SandboxSummary("stopped-one", false)
            );
    }

    [Fact]
    public async Task TheTemplatesAreReadFromTheJsonOutput()
    {
        // Arrange
        Answer(
            ["template", "ls", "--json"],
            new ProcessResult(
                0,
                """
                [
                  {
                    "id": "527ef0280e47",
                    "repository": "docker.io/library/sandbox-kit-assembled",
                    "tag": "0307199587cd420e",
                    "flavor": "shell-docker",
                    "created_at": "2026-11-08T14:49:51Z",
                    "size": 1389240008
                  },
                  {
                    "id": "ecc48f9d23bd",
                    "repository": "docker.io/library/sbx-kit-src",
                    "tag": "pi-config-eacbe0b12b58",
                    "created_at": "2026-11-08T14:55:39Z",
                    "size": 6957
                  },
                  { "id": "untagged", "repository": "docker.io/library/sbx-kit-src" }
                ]
                """,
                string.Empty
            )
        );

        // Act
        var templates = await _cleanup.ListTemplatesAsync(CancellationToken.None);

        // Assert
        templates
            .Should()
            .Equal(
                new TemplateImage(
                    "docker.io/library/sandbox-kit-assembled:0307199587cd420e",
                    TemplateKind.AssembledSet,
                    "0307199587cd420e",
                    "527ef0280e47",
                    DateTimeOffset.Parse("2026-11-08T14:49:51Z")
                ),
                new TemplateImage(
                    "docker.io/library/sbx-kit-src:pi-config-eacbe0b12b58",
                    TemplateKind.KitSource,
                    string.Empty,
                    "ecc48f9d23bd",
                    DateTimeOffset.Parse("2026-11-08T14:55:39Z")
                )
            );
    }

    [Theory]
    [InlineData("sandbox-kit-assembled", "new", TemplateKind.AssembledSet, "new")]
    [InlineData("sandbox-kit-assembled", "new-kit-0", TemplateKind.AssembledSet, "new")]
    [InlineData("sandbox-kit-assembled", "new-kit-12", TemplateKind.AssembledSet, "new")]
    [InlineData("sandbox-kit-assembled", "new-template", TemplateKind.AssembledSet, "new")]
    [InlineData("sandbox-kit-assembled", "new-kit-a", TemplateKind.AssembledSet, "new-kit-a")]
    [InlineData("docker.io/library/sandbox-kit-assembled", "new", TemplateKind.AssembledSet, "new")]
    [InlineData(
        "registry.example:5000/runtime/sandbox-kit-assembled",
        "new",
        TemplateKind.AssembledSet,
        "new"
    )]
    [InlineData("sbx-kit-src", "unused", TemplateKind.KitSource, "")]
    [InlineData("docker.io/library/sbx-kit-src", "unused", TemplateKind.KitSource, "")]
    [InlineData("other-sandbox-kit-assembled", "old", TemplateKind.Unrelated, "")]
    [InlineData("docker.io/library/other-sbx-kit-src", "unused", TemplateKind.Unrelated, "")]
    [InlineData("sandbox-kit-assembled-other", "old", TemplateKind.Unrelated, "")]
    [InlineData("sbx-kit-src-other", "unused", TemplateKind.Unrelated, "")]
    [InlineData("agentic-box-rwest-team/squad", "local", TemplateKind.Unrelated, "")]
    public async Task TheTemplatesAreClassifiedByExactRepositoryAndAssembledSet(
        string repository,
        string tag,
        TemplateKind kind,
        string set
    )
    {
        // Arrange
        Answer(
            ["template", "ls", "--json"],
            new ProcessResult(
                0,
                $$"""[{ "id": "id", "repository": "{{repository}}", "tag": "{{tag}}" }]""",
                string.Empty
            )
        );

        // Act
        var templates = await _cleanup.ListTemplatesAsync(CancellationToken.None);

        // Assert
        templates.Should().Equal(new TemplateImage($"{repository}:{tag}", kind, set, "id", null));
    }

    [Fact]
    public async Task AFailingInventoryIsAnError()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "daemon down"));

        // Act
        var act = () => _cleanup.ListTemplatesAsync(CancellationToken.None);

        // Assert
        (await act.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be("'sbx template ls' failed: daemon down");
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task StoppingASandboxReportsWhetherItStopped(int exitCode, bool expected)
    {
        // Arrange
        Answer(["stop", "sample"], new ProcessResult(exitCode, string.Empty, "stderr"));

        // Act
        var stopped = await _cleanup.StopSandboxAsync("sample", CancellationToken.None);

        // Assert
        stopped.Should().Be(expected);
    }

    [Fact]
    public async Task ARemovedTemplateIsRemovedByReferenceWithoutAsking()
    {
        // Arrange
        Answer(
            ["template", "rm", "docker.io/library/sbx-kit-src:old", "--force"],
            new ProcessResult(0, "deleted", string.Empty)
        );

        // Act
        var removal = await _cleanup.RemoveTemplateAsync(
            new TemplateImage(
                "docker.io/library/sbx-kit-src:old",
                TemplateKind.KitSource,
                string.Empty,
                "id",
                null
            ),
            CancellationToken.None
        );

        // Assert
        removal.Should().Be(TemplateRemoval.Done);
    }

    [Fact]
    public async Task ARefusedRemovalCarriesTheTrimmedRefusal()
    {
        // Arrange
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessResult(17, "", "image is used by a sandbox\n")
                {
                    Output = "  image is used by a sandbox\n",
                }
            );

        // Act
        var removal = await _cleanup.RemoveTemplateAsync(
            new TemplateImage("sbx-kit-src:old", TemplateKind.KitSource, string.Empty, "id", null),
            CancellationToken.None
        );

        // Assert
        removal.Should().Be(new TemplateRemoval(false, "image is used by a sandbox"));
    }

    private void Answer(string[] arguments, ProcessResult result) =>
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "sbx" && request.Arguments.SequenceEqual(arguments)
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(result);
}
