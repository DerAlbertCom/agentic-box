namespace Abox.Infrastructure.Shell;

/// <summary>
/// Finds a POSIX <c>sh.exe</c> on Windows: one on the PATH, else the one Git for Windows
/// installs next to the <c>git.exe</c> on the PATH, else the one of its default install.
/// </summary>
internal static class GitForWindowsShell
{
    public static string? Find() =>
        Find(
            Environment.GetEnvironmentVariable("PATH") ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        );

    internal static string? Find(string pathDirectories, string programFiles)
    {
        var directories = pathDirectories.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        return Candidates(directories, programFiles).FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> Candidates(string[] directories, string programFiles)
    {
        foreach (var directory in directories)
        {
            yield return Path.Combine(directory, "sh.exe");
        }

        // git.exe lies in <Git>\cmd or <Git>\<mingw64|ucrt64>\bin, its sh.exe in <Git>\usr\bin.
        foreach (var directory in directories.Where(d => File.Exists(Path.Combine(d, "git.exe"))))
        {
            yield return Path.GetFullPath(Path.Combine(directory, "..", "usr", "bin", "sh.exe"));
            yield return Path.GetFullPath(
                Path.Combine(directory, "..", "..", "usr", "bin", "sh.exe")
            );
        }

        yield return Path.Combine(programFiles, "Git", "usr", "bin", "sh.exe");
    }
}
