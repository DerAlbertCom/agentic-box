using Abox.Features.Shared.Preflight;

namespace Abox.Infrastructure.Shell;

/// <summary>Finds programs in the directories of the host's PATH, as a shell would.</summary>
public sealed class ShellHostPrograms(string pathDirectories) : IHostPrograms
{
    public ShellHostPrograms()
        : this(Environment.GetEnvironmentVariable("PATH") ?? string.Empty) { }

    public bool Exists(string program)
    {
        if (string.IsNullOrWhiteSpace(program))
        {
            return false;
        }

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries
            )
            : [string.Empty];

        foreach (
            var directory in pathDirectories.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, program + extension);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
