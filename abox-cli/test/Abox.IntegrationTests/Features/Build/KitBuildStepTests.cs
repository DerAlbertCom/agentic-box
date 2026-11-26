using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Abox.Tests;

public sealed class KitBuildStepTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private IKitBuildScript Script => _fixture.BuildScript;
    private BuildStore Store => _fixture.Store;
    private SquadResolver Resolver => _fixture.Resolver;
    private BuildService Service => _fixture.Service;

    [Fact]
    public async Task TheKitImageIsBuiltFromTheStagedBuiltKit()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        Root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(Root, "built", "exit 0\n");
        Script
            .When(script =>
                script.RunAsync(Arg.Any<KitBuildScriptRun>(), Arg.Any<CancellationToken>())
            )
            .Do(call =>
                File.WriteAllText(
                    Path.Combine(call.Arg<KitBuildScriptRun>().StagedDirectory, "generated.txt"),
                    "generated\n"
                )
            );

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var kit = Manifest().Kits.Should().ContainSingle(kit => kit.Name == "built").Subject;
        kit.Reference.Should().Be(Path.Combine(Root.Path, "kits", "built"));
        var staged = Staged();
        staged.Should().NotBe(Path.Combine(Root.Path, "kits", "built"));
        Path.GetFileName(staged).Should().Be("built");
        File.ReadAllText(Path.Combine(staged, "generated.txt")).Should().Be("generated\n");
        File.Exists(Path.Combine(staged, "built.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(Root.Path, "kits", "built", "generated.txt")).Should().BeFalse();
        BuiltFrom().Should().Contain(staged);
    }

    [Fact]
    public async Task AnUnchangedKitDoesNotRunItsBuildStepAgain()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        Root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(Root, "built", "exit 0\n");
        await Service.BuildAsync(Request(), CancellationToken.None);
        var staged = Staged();

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        await Script
            .Received(1)
            .RunAsync(Arg.Any<KitBuildScriptRun>(), Arg.Any<CancellationToken>());
        Staged().Should().Be(staged);
        Directory.Exists(staged).Should().BeTrue();
    }

    [Fact]
    public async Task AChangeInTheKitFolderRunsItsBuildStepAgain()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        Root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(Root, "built", "exit 0\n");
        await Service.BuildAsync(Request(), CancellationToken.None);
        Root.File("kits/built/src/main.ts", "changed\n");

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        await Script
            .Received(2)
            .RunAsync(Arg.Any<KitBuildScriptRun>(), Arg.Any<CancellationToken>());
        File.Exists(Path.Combine(Staged(), "src", "main.ts")).Should().BeTrue();
    }

    [Fact]
    public async Task TheBuildFileRunsIntoTheStagedCopyFromTheKitFolder()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        Root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        var build = KitBuildScripts.Write(Root, "built", "exit 0\n");

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var kit = Manifest().Kits.Should().ContainSingle(kit => kit.Name == "built").Subject;
        kit.Reference.Should().Be(Path.Combine(Root.Path, "kits", "built"));
        var staged = Staged();
        await Script
            .Received(1)
            .RunAsync(
                new KitBuildScriptRun(
                    "built",
                    build,
                    staged,
                    Path.Combine(Root.Path, "kits", "built")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AFailingBuildFailsTheBuild()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        Root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(Root, "built", "exit 3\n");
        Script
            .RunAsync(Arg.Any<KitBuildScriptRun>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AboxException("The build of kit 'built' failed"));

        // Act
        var build = () => Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        await build.Should().ThrowAsync<AboxException>().WithMessage("*kit 'built' failed*");
        Store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
    }

    [Fact]
    public async Task ABuildFileThatIsNotExecutableFailsTheBuild()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Windows runs the build file with sh."
            );
        }

        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [plain]\n");
        Root.File("kits/plain/plain.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        var script = Root.File("kits/plain/build", "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // Act
        var build = () => Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var failure = await build.Should().ThrowAsync<AboxException>();
        failure.WithMessage("*build file of kit 'plain'*not executable*chmod +x*");
        Store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
        await Script.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        // The earlier workload component is valid; the failing kit and later components never build.
        BuiltFrom()
            .Should()
            .Equal(
                Path.Combine(
                    Store.DirectoryFor(new SquadBuildIdentity("sample", "ac", "latest")),
                    "workload"
                )
            );
    }

    [Fact]
    public async Task KitsWithoutABuildFilePassThroughUnchanged()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nkits: [plain]\n");
        Root.File("kits/plain/plain.yaml", "schemaVersion: \"3\"\nkind: mixin\n");

        // Act
        await Service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var source = Path.Combine(Root.Path, "kits", "plain");
        Manifest()
            .Kits.Should()
            .ContainSingle(kit => kit.Name == "plain")
            .Which.Reference.Should()
            .Be(source);
        await Script.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
        BuiltFrom().Should().Contain(source);
    }

    /// <summary>The directories the synthetic Docker built component images from.</summary>
    private List<string> BuiltFrom() =>
        _fixture
            .Runner.ReceivedCalls()
            .Select(call => (ProcessRequest)call.GetArguments()[0]!)
            .Where(request => request.Arguments.Contains("--load"))
            .Select(request => request.Arguments[^1])
            .ToList();

    private BuildRequest Request() =>
        new([Resolver.ResolveTeam(Root.Path, "sample")], Root.Path, "latest");

    /// <summary>The directory the kit's component image was built from: its staged copy.</summary>
    private string Staged() => Manifest().Component("kits/built")!.Directory;

    private BuildManifest Manifest() =>
        Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
}
