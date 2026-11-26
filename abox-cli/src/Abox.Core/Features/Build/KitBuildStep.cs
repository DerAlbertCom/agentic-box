using Abox.Features.Shared.Common;
using Abox.Features.Shared.KitBuildScripts;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Build;

/// <summary>
/// The kit build step: a local kit with a <c>build</c> file is copied into the build's
/// staging folder and built there, and the staged copy is what its component image is
/// built from. Kits without a <c>build</c> file need no step; a <c>build</c> file that is
/// not executable fails the build. On Windows the build file runs with sh, so it needs no
/// mode. Only the staged copy of the kit that is built again is replaced.
/// </summary>
internal sealed class KitBuildStep(IKitBuildScript script, IConsole console)
{
    /// <summary>The executable file that marks a kit with a build step.</summary>
    public const string BuildFileName = "build";

    /// <summary>The folder of the build directory that holds the staged kits.</summary>
    public const string StagingFolderName = "kits";

    /// <summary>Whether the local kit has a build step.</summary>
    public static bool HasBuildStep(KitSource kit) =>
        kit.Directory is { } source && File.Exists(Path.Combine(source, BuildFileName));

    /// <summary>
    /// The directory the kit's image is built from: the staged copy, built again, for a kit
    /// with a build step, otherwise the kit's own folder.
    /// </summary>
    public async Task<string> BuildAsync(
        KitSource kit,
        string buildDirectory,
        CancellationToken cancellationToken
    )
    {
        var source =
            kit.Directory
            ?? throw new InvalidOperationException($"The kit '{kit.Name}' is not local.");
        if (!HasBuildStep(kit))
        {
            return source;
        }

        EnsureRunnable(kit.Name, Path.Combine(source, BuildFileName));

        var staged = Path.Combine(
            buildDirectory,
            StagingFolderName,
            Path.GetFileName(Path.TrimEndingDirectorySeparator(source))
        );
        if (Directory.Exists(staged))
        {
            Directory.Delete(staged, recursive: true);
        }
        CopyDirectory(source, staged);

        console.WriteLine($"Building kit '{kit.Name}'...");
        await script.RunAsync(
            new KitBuildScriptRun(kit.Name, Path.Combine(source, BuildFileName), staged, source),
            cancellationToken
        );

        return staged;
    }

    private static void EnsureRunnable(string kit, string build)
    {
        // Windows has no executable bit; the build file runs with sh there.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        if (!File.GetUnixFileMode(build).HasFlag(UnixFileMode.UserExecute))
        {
            throw new AboxException(
                $"The build file of kit '{kit}' is not executable: {build}. Make it executable with 'chmod +x', or remove it if the kit has no build step."
            );
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var copied = Path.Combine(destination, Path.GetFileName(entry));
            var info = new FileInfo(entry);
            if (info.LinkTarget is { } target)
            {
                File.CreateSymbolicLink(copied, target);
            }
            else if (Directory.Exists(entry))
            {
                CopyDirectory(entry, copied);
            }
            else
            {
                File.Copy(entry, copied);
            }

            if (info.LinkTarget is null && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(copied, File.GetUnixFileMode(entry));
            }
        }
    }
}
