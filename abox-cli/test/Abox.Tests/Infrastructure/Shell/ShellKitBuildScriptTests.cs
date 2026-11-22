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
        var build = KitBuildScripts.Write(
            _root,
            "built",
            "printf '%s\\n%s\\n' \"$PWD\" \"$1\" > \"$1/invocation.txt\"\n"
        );
        var staged = Directory.CreateDirectory(Path.Combine(_root.Path, "staged")).FullName;
        var kit = Path.Combine(_root.Path, "kits", "built");

        // Act
        await _script.RunAsync(new KitBuildScriptRun("built", build, staged, kit), default);

        // Assert
        File.ReadAllLines(Path.Combine(staged, "invocation.txt")).Should().Equal(kit, staged);
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
}
