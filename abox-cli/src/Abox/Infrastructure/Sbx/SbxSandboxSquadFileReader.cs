using Abox.Features.Shared.Credentials;

namespace Abox.Infrastructure.Sbx;

/// <summary>Reads the squad files of a sandbox through <c>sbx exec</c>.</summary>
public sealed class SbxSandboxSquadFileReader(IProcessRunner runner) : ISandboxSquadFileReader
{
    private const string Script = """
        for descriptor in /usr/share/sandbox/kit/*/kit.yaml; do
          [ -f "$descriptor" ] || continue
          kit=${descriptor%/kit.yaml}
          printf 'kit '
          printf %s "${kit##*/}" | base64 | tr -d '\n'
          printf ' '
          base64 < "$descriptor" | tr -d '\n'
          printf '\n'
        done
        if [ -f /usr/share/abox/squad.json ]; then
          printf 'squad '
          base64 < /usr/share/abox/squad.json | tr -d '\n'
          printf '\n'
        fi
        """;

    private readonly SbxInvoker _invoker = new(runner);

    /// <summary>
    /// One exec, which starts a stopped sandbox, prints each file on its own line as
    /// <c>kit</c>, its base64-encoded folder name and base64-encoded content, or
    /// <c>squad</c> and the base64-encoded <c>squad.json</c>, so no name or content can
    /// break the framing.
    /// </summary>
    public async Task<SandboxSquadFiles> ReadAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var result = await _invoker.RunOrThrowAsync(
            new ProcessRequest(
                SbxInvoker.Executable,
                ["exec", sandbox, "sh", "-c", Script],
                StandardInput: string.Empty
            ),
            $"sbx exec {sandbox}",
            cancellationToken
        );

        var kits = new List<StagedKitDescriptor>();
        string? squadJson = null;
        foreach (var line in result.StandardOutput.Split('\n'))
        {
            var fields = line.TrimEnd('\r').Split(' ');
            if (fields is ["kit", var name, var descriptor])
                kits.Add(
                    new StagedKitDescriptor(Decode(name, sandbox), Decode(descriptor, sandbox))
                );
            else if (fields is ["squad", var squad])
                squadJson = Decode(squad, sandbox);
        }

        return new SandboxSquadFiles(kits, squadJson);
    }

    private static string Decode(string base64, string sandbox)
    {
        try
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException exception)
        {
            throw new AboxException(
                $"Cannot read the squad metadata of the sandbox '{sandbox}': the output of 'sbx exec' is garbled.",
                exception
            );
        }
    }
}
