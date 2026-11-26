namespace Abox.Features.Shared.Registry;

/// <summary>Resolves registry references to the digest they point to now.</summary>
public interface IRegistryDigests
{
    /// <summary>
    /// The digest <paramref name="reference"/> resolves to in its registry. Fails when the
    /// reference cannot be read.
    /// </summary>
    Task<string> DigestAsync(string reference, CancellationToken cancellationToken);
}
