namespace Abox.Tests;

/// <summary>Writes the executable <c>build</c> file of a local kit.</summary>
internal static class KitBuildScripts
{
    internal static string Write(TempDirectory root, string kit, string body)
    {
        var path = root.File($"kits/{kit}/build", "#!/bin/sh\nset -e\n" + body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }

        return Path.GetFullPath(path);
    }
}
