using System.Text.RegularExpressions;
using Abox.Features.Shared.Squads;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// The arguments of <c>sbx secret set-custom</c> for a custom secret: the ones
/// <see cref="SbxHostSecretStore"/> passes, and the command line abox prints when the user has to
/// run it with a host command abox does not take from a sandbox.
/// </summary>
internal static partial class SbxCustomSecretArguments
{
    /// <summary>
    /// The <c>sbx secret set-custom</c> command line that stores a secret from a host
    /// command, quoted for a POSIX shell.
    /// </summary>
    /// <param name="declaration">The custom secret.</param>
    /// <param name="command">The host command, or a stand-in for it.</param>
    public static string CommandLine(SecretDeclaration declaration, string command) =>
        string.Join(
            ' ',
            ["sbx", .. For(declaration).Append("--command").Append(command).Select(Quote)]
        );

    /// <summary>The arguments without the value source.</summary>
    internal static List<string> For(SecretDeclaration declaration)
    {
        var arguments = new List<string> { "secret", "set-custom" };

        foreach (var host in declaration.Hosts ?? [])
        {
            arguments.Add("--host");
            arguments.Add(host);
        }

        if (declaration.EnvironmentVariable is not null)
        {
            arguments.Add("--env");
            arguments.Add(declaration.EnvironmentVariable);
        }

        if (declaration.Placeholder is not null)
        {
            arguments.Add("--placeholder");
            arguments.Add(declaration.Placeholder);
        }

        return arguments;
    }

    private static string Quote(string argument) =>
        PlainShellWord().IsMatch(argument)
            ? argument
            : "'" + argument.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    [GeneratedRegex(@"^[A-Za-z0-9_./:=@%+-]+$")]
    private static partial Regex PlainShellWord();
}
