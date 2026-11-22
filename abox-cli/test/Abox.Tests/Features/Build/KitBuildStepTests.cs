using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.KitInspection;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Abox.Tests;

public sealed class KitBuildStepTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private IKitInspector _inspector => _fixture.Inspector;
    private IKitBuildScript _script => _fixture.BuildScript;
    private BuildStore _store => _fixture.Store;
    private SquadResolver _resolver => _fixture.Resolver;
    private BuildService _service => _fixture.Service;

    [Fact]
    public async Task SbxReceivesTheStagedBuiltKit()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(_root, "built", "exit 0\n");
        _script
            .When(script =>
                script.RunAsync(Arg.Any<KitBuildScriptRun>(), Arg.Any<CancellationToken>())
            )
            .Do(call =>
                File.WriteAllText(
                    Path.Combine(call.Arg<KitBuildScriptRun>().StagedDirectory, "generated.txt"),
                    "generated\n"
                )
            );
        var inspected = new List<string>();
        _inspector
            .InspectAsync(
                Arg.Do<string>(inspected.Add),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new KitInspectionOutcome(true, ""));

        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var kit = Manifest().Kits.Should().ContainSingle(kit => kit.Name == "built").Subject;
        kit.Reference.Should().NotBe(Path.Combine(_root.Path, "kits", "built"));
        Path.GetFileName(kit.Reference).Should().Be("built");
        File.ReadAllText(Path.Combine(kit.Reference, "generated.txt")).Should().Be("generated\n");
        File.Exists(Path.Combine(kit.Reference, "built.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_root.Path, "kits", "built", "generated.txt")).Should().BeFalse();
        inspected.Should().Contain(kit.Reference);
    }

    [Fact]
    public async Task TheBuildFileRunsIntoTheStagedCopyFromTheKitFolder()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        var build = KitBuildScripts.Write(_root, "built", "exit 0\n");

        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var kit = Manifest().Kits.Should().ContainSingle(kit => kit.Name == "built").Subject;
        await _script
            .Received(1)
            .RunAsync(
                new KitBuildScriptRun(
                    "built",
                    build,
                    kit.Reference,
                    Path.Combine(_root.Path, "kits", "built")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AFailingBuildFailsTheBuild()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(_root, "built", "exit 3\n");
        _script
            .RunAsync(Arg.Any<KitBuildScriptRun>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AboxException("The build of kit 'built' failed"));

        // Act
        var build = () => _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        await build.Should().ThrowAsync<AboxException>().WithMessage("*kit 'built' failed*");
        _store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
    }

    [Fact]
    public async Task ABuildFileThatCannotRunFailsTheBuild()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [plain]\n");
        _root.File("kits/plain/plain.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        _root.File("kits/plain/build", "#!/bin/sh\nexit 0\n");

        // Act
        var build = () => _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var failure = await build.Should().ThrowAsync<AboxException>();
        failure.WithMessage(
            OperatingSystem.IsWindows()
                ? "*kit 'plain'*build step*Linux or macOS*"
                : "*build file of kit 'plain'*not executable*chmod +x*"
        );
        _store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
        await _script.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _inspector.DidNotReceiveWithAnyArgs().InspectAsync(default!, default!, default);
    }

    [Fact]
    public async Task KitsWithoutABuildFilePassThroughUnchanged()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [plain]\n");
        _root.File("kits/plain/plain.yaml", "schemaVersion: \"3\"\nkind: mixin\n");

        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var source = Path.Combine(_root.Path, "kits", "plain");
        Manifest()
            .Kits.Should()
            .ContainSingle(kit => kit.Name == "plain")
            .Which.Reference.Should()
            .Be(source);
        await _script.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        await _inspector
            .Received(1)
            .InspectAsync(
                source,
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>()
            );
    }

    private BuildRequest Request() =>
        new([_resolver.ResolveTeam(_root.Path, "sample")], _root.Path, "latest");

    private BuildManifest Manifest() =>
        _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
}
