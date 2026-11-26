using Abox.Infrastructure;
using Abox.Infrastructure.Shell;

namespace Abox.Tests;

public sealed class ShellFolderRealPathTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ShellFolderRealPath _realPaths = new(new ProcessRunner());

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ALinkedFolderResolvesToItsTarget()
    {
        // Arrange
        var target = _root.CreateDirectory("target");
        var link = Path.Combine(_root.Path, "link");
        using var links = new DirectoryLinks();
        await links.Add(link, target);

        // Act
        var realPath = await _realPaths.ResolveAsync(link, CancellationToken.None);

        // Assert
        realPath.Should().Be(await _realPaths.ResolveAsync(target, CancellationToken.None));
        Path.GetFileName(realPath).Should().Be("target");
    }

    [Fact]
    public async Task AMissingFolderIsItsOwnRealPath()
    {
        // Arrange
        var missing = Path.Combine(_root.Path, "missing; echo injected");

        // Act
        var realPath = await _realPaths.ResolveAsync(missing, CancellationToken.None);

        // Assert
        realPath.Should().Be(OperatingSystem.IsWindows() ? Path.GetFullPath(missing) : missing);
    }
}
