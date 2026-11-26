using Abox.Features.Shared.Common;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Infrastructure;
using Abox.Infrastructure.Shell;

namespace Abox.Tests;

public sealed class ShellKitBuildScriptTests
{
    private readonly TempDirectory _root = new();
    private readonly ShellKitBuildScript _script = new(new ProcessRunner());

    [Fact]
    public async Task TheBuildFileGetsTheStagedFolderAndRunsFromTheKitFolder()
    {
        // Arrange
        var build = KitBuildScripts.Write(_root, "built", "cp marker.txt \"$1/\"\n");
        _root.File("kits/built/marker.txt", "from the kit folder");
        var staged = Directory.CreateDirectory(Path.Combine(_root.Path, "staged")).FullName;
        var kit = Path.Combine(_root.Path, "kits", "built");

        // Act
        await _script.RunAsync(new KitBuildScriptRun("built", build, staged, kit), default);

        // Assert
        File.ReadAllText(Path.Combine(staged, "marker.txt")).Should().Be("from the kit folder");
    }

    [Fact]
    public async Task AFailingBuildFailsWithTheKitTheExitCodeAndTheOutputOfTheBuildFile()
    {
        // Arrange
        var build = KitBuildScripts.Write(
            _root,
            "built",
            "echo compiling\necho 'syntax error in main.ts' >&2\nexit 3\n"
        );
        var staged = Directory.CreateDirectory(Path.Combine(_root.Path, "staged")).FullName;
        var kit = Path.Combine(_root.Path, "kits", "built");

        // Act
        var run = () =>
            _script.RunAsync(new KitBuildScriptRun("built", build, staged, kit), default);

        // Assert
        (await run.Should().ThrowAsync<AboxException>()).WithMessage(
            "The build of kit 'built' failed with exit code 3:*compiling*syntax error in main.ts"
        );
    }

    [Fact]
    public async Task OnWindowsWithoutAShellTheBuildFailsNamingGitForWindows()
    {
        // Arrange
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Only Windows runs the build file with sh."
            );
        }

        var script = new ShellKitBuildScript(new ProcessRunner(), () => null);
        var build = KitBuildScripts.Write(_root, "built", "exit 0\n");
        var kit = Path.Combine(_root.Path, "kits", "built");

        // Act
        var run = () =>
            script.RunAsync(new KitBuildScriptRun("built", build, _root.Path, kit), default);

        // Assert
        (await run.Should().ThrowAsync<AboxException>()).WithMessage(
            "*kit 'built'*Git for Windows*"
        );
    }
}
