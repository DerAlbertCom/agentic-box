using Abox.Infrastructure.Shell;

namespace Abox.Tests;

public sealed class ShellHostProgramsTests
{
    private static readonly string Extension = OperatingSystem.IsWindows() ? ".EXE" : "";

    [Theory]
    [InlineData("tool", true)]
    [InlineData("missing", false)]
    [InlineData(" ", false)]
    public void AProgramExistsWhenADirectoryOfThePathHoldsIt(string program, bool expected)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("bin/tool" + Extension, "");
        var path = string.Join(
            Path.PathSeparator,
            Path.Combine(root.Path, "empty"),
            Path.Combine(root.Path, "bin")
        );

        // Act
        var exists = new ShellHostPrograms(path).Exists(program);

        // Assert
        exists.Should().Be(expected);
    }
}
