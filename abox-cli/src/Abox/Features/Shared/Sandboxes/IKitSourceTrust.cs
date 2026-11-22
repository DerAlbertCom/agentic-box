namespace Abox.Features.Shared.Sandboxes;

/// <summary>Which registry repositories the sandbox runtime accepts kits from.</summary>
public interface IKitSourceTrust
{
    /// <summary>
    /// Trusts a repository-scoped prefix such as <c>ghcr.io/team/</c> unless it is covered
    /// already, preserving every other trusted source. Fails when the prefix is broad, when
    /// the trust is controlled elsewhere, or when the change does not take effect.
    /// </summary>
    Task EnsureTrustedAsync(string repositoryPrefix, CancellationToken cancellationToken);
}
