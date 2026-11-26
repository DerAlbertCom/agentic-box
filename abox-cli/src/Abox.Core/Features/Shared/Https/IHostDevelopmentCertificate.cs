using System.Security.Cryptography;

namespace Abox.Features.Shared.Https;

/// <summary>The host's existing trusted ASP.NET Core development certificate.</summary>
public interface IHostDevelopmentCertificate
{
    /// <summary>
    /// Exports the one exact trusted identity, protected with <paramref name="password"/>; never
    /// generates, renews or trusts a certificate. Fails with an actionable message otherwise.
    /// </summary>
    Task<ExportedDevelopmentCertificate> ExportAsync(string password, CancellationToken token);
}

public sealed class ExportedDevelopmentCertificate : IDisposable
{
    private readonly byte[] _pfx;

    public ExportedDevelopmentCertificate(byte[] pfx, string password)
    {
        _pfx = pfx;
        Password = password;
    }

    public ReadOnlyMemory<byte> Pfx => _pfx;
    public string Password { get; }

    public override string ToString() => "ExportedDevelopmentCertificate (private material hidden)";

    public void Dispose() => CryptographicOperations.ZeroMemory(_pfx);
}
