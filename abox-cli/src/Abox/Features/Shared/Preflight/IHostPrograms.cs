namespace Abox.Features.Shared.Preflight;

/// <summary>
/// Finds programs on the host's PATH.
/// </summary>
public interface IHostPrograms
{
    bool Exists(string program);
}
