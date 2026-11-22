namespace Abox.Features.Shared.Credentials;

/// <summary>Reads the squad files a sandbox carries.</summary>
public interface ISandboxSquadFileReader
{
    /// <summary>
    /// Reads every staged kit descriptor and the bundled squad metadata of a sandbox at once,
    /// starting the sandbox when it is stopped.
    /// </summary>
    Task<SandboxSquadFiles> ReadAsync(string sandbox, CancellationToken cancellationToken);
}
