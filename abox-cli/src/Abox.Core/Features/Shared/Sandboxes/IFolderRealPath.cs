namespace Abox.Features.Shared.Sandboxes;

/// <summary>The physical path of a host folder, with every symbolic link resolved.</summary>
public interface IFolderRealPath
{
    /// <summary>The real path of the folder, or the location itself when it cannot be resolved.</summary>
    Task<string> ResolveAsync(string location, CancellationToken cancellationToken);
}
