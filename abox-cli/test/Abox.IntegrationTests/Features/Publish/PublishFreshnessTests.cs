using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishFreshnessTests
{
    private static readonly string Moved = "sha256:" + new string('9', 64);
    private readonly PublishServiceFixture _fixture = new();

    private BuildManifest Build(string squad = "ac") =>
        _fixture.Store.Load(new SquadBuildIdentity("sample", squad, "latest"))!;

    private void ResolveTo(string reference, string digest) =>
        _fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("{{json .Manifest}}")
                    && request.Arguments.Last() == reference
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, JsonSerializer.Serialize(new { digest }), ""));

    /// <summary>Publishes two squads, so a stale second squad proves nothing was pushed for the first.</summary>
    private async Task<AboxException> PublishStaleAsync()
    {
        var action = () =>
            _fixture.Service.PublishAsync(_fixture.Request("ac", "bd"), CancellationToken.None);
        return (await action.Should().ThrowAsync<AboxException>()).Which;
    }

    private void AssertNothingPushed()
    {
        _fixture
            .Calls.Should()
            .NotContain(call =>
                call.Arguments.Contains("push")
                || call.Arguments.Contains("--push")
                || call.Arguments.Contains("create")
            );
    }

    [Fact]
    public async Task AMovedBaseImageDigestIsRefusedBeforeAnyPush()
    {
        // Arrange
        _fixture.SaveBuild("ac");
        _fixture.SaveBuild("bd");
        var build = Build("bd");
        build.Base = new PinnedImage
        {
            Reference = "example.com/base:1",
            Digest = LocalWorkloadFactory.Digest("example.com/base:1"),
        };
        _fixture.SaveVerifiedBuild(build);
        ResolveTo("example.com/base:1", Moved);

        // Act
        var failure = await PublishStaleAsync();

        // Assert
        failure
            .Message.Should()
            .Match(
                $"The base image 'example.com/base:1' now resolves to {Moved}*Run 'abox build --team sample --tag latest' before publishing."
            );
        AssertNothingPushed();
    }

    [Fact]
    public async Task AMovedRegistryKitDigestIsRefusedBeforeAnyPush()
    {
        // Arrange
        _fixture.SaveBuild("ac");
        _fixture.SaveBuild("bd");
        var build = Build("bd");
        build.Kits.Add(
            new ManifestKit
            {
                Name = "tool",
                Reference = "ghcr.io/example/tool:1",
                Digest = LocalWorkloadFactory.Digest("ghcr.io/example/tool:1"),
            }
        );
        _fixture.SaveVerifiedBuild(build);
        ResolveTo("ghcr.io/example/tool:1", Moved);

        // Act
        var failure = await PublishStaleAsync();

        // Assert
        failure
            .Message.Should()
            .Match(
                $"The registry kit 'ghcr.io/example/tool:1' now resolves to {Moved}*abox build*"
            );
        AssertNothingPushed();
    }

    [Fact]
    public async Task AChangedKitRepositoryFolderIsRefusedBeforeAnyPush()
    {
        // Arrange
        _fixture.SaveBuild("ac");
        _fixture.SaveBuild("bd");
        // A kit with a build step: built from its staged copy, its inputs are its repository folder.
        var source = _fixture.Root.File("kits/local/local.yaml", "kind: mixin\n");
        var staged = _fixture.Root.File("staged/local/local.yaml", "kind: mixin\n");
        var build = Build("bd");
        build.Kits.Add(
            new ManifestKit
            {
                Name = "local",
                Reference = Path.GetDirectoryName(source)!,
                IsLocal = true,
            }
        );
        LocalWorkloadFactory.WithImages(_fixture.Root, build);
        build.Component("kits/local")!.Directory = Path.GetDirectoryName(staged)!;
        _fixture.SaveVerifiedBuild(build);
        _fixture.Root.File("kits/local/src/main.ts", "changed\n");

        // Act
        var failure = await PublishStaleAsync();

        // Assert
        failure.Message.Should().Match("The kit 'local' of 'bd' changed since its build.*");
        AssertNothingPushed();
    }

    [Fact]
    public async Task AReplacedComponentImageIsRefusedBeforeAnyPush()
    {
        // Arrange
        _fixture.SaveBuild("ac");
        _fixture.SaveBuild("bd");
        _fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("{{.Id}}")
                    && request.Arguments.Last().Contains("/bd/components/workload:")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, Moved, ""));

        // Act
        var failure = await PublishStaleAsync();

        // Assert
        failure
            .Message.Should()
            .Match("The component image '*/bd/components/workload:*' is missing or was replaced.*");
        AssertNothingPushed();
    }

    [Fact]
    public async Task AComponentWhoseRegistryDigestDiffersFromTheBuildIsNeverPromoted()
    {
        // Arrange
        _fixture.SaveBuild("ac");
        var workload = Build().Components.Single(component => component.Name == "workload");
        _fixture
            .Runner.RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                // From now on the registry reports another digest for the pushed reference.
                ResolveTo(workload.Reference, Moved);
                return new ProcessResult(0, "", "");
            });

        // Act
        var action = () =>
            _fixture.Service.PublishAsync(_fixture.Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            $"The registry holds '{workload.Reference}' as {Moved}, not as the built {workload.Digest}*"
        );
        _fixture.Calls.Should().NotContain(call => call.Arguments.Contains("create"));
    }
}
