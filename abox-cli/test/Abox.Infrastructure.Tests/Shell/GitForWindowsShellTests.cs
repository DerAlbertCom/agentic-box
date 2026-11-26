using Abox.Infrastructure.Shell;

namespace Abox.Tests;

public sealed class GitForWindowsShellTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void AShellOnThePathComesFirst()
    {
        // Arrange
        var shell = _root.File("msys/bin/sh.exe", "");
        _root.File("ProgramFiles/Git/usr/bin/sh.exe", "");

        // Act
        var found = GitForWindowsShell.Find(
            Path.Combine(_root.Path, "msys", "bin"),
            Path.Combine(_root.Path, "ProgramFiles")
        );

        // Assert
        found.Should().Be(Path.GetFullPath(shell));
    }

    [Theory]
    [InlineData("cmd")]
    [InlineData("mingw64/bin")]
    public void TheShellOfTheGitOnThePathIsFound(string gitFolder)
    {
        // Arrange
        _root.File($"Git/{gitFolder}/git.exe", "");
        var shell = _root.File("Git/usr/bin/sh.exe", "");

        // Act
        var found = GitForWindowsShell.Find(
            Path.Combine(_root.Path, "Git", gitFolder),
            Path.Combine(_root.Path, "ProgramFiles")
        );

        // Assert
        found.Should().Be(Path.GetFullPath(shell));
    }

    [Fact]
    public void TheDefaultInstallOfGitIsTheLastResort()
    {
        // Arrange
        var shell = _root.File("ProgramFiles/Git/usr/bin/sh.exe", "");

        // Act
        var found = GitForWindowsShell.Find(
            Path.Combine(_root.Path, "empty"),
            Path.Combine(_root.Path, "ProgramFiles")
        );

        // Assert
        found.Should().Be(Path.GetFullPath(shell));
    }

    [Fact]
    public void WithoutAnyShellNothingIsFound()
    {
        // Act
        var found = GitForWindowsShell.Find(
            Path.Combine(_root.Path, "empty"),
            Path.Combine(_root.Path, "ProgramFiles")
        );

        // Assert
        found.Should().BeNull();
    }
}
