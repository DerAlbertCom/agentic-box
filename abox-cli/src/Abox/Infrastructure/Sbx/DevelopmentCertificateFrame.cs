using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Abox.Infrastructure.Sbx;

/// <summary>Validates and frames private certificate data without invoking the runtime.</summary>
internal static class DevelopmentCertificateFrame
{
    /// <summary>The framed payload, or null when the payload cannot be transferred.</summary>
    internal static string? Create(
        ReadOnlyMemory<byte> pfx,
        string password,
        string? publicCertificate
    )
    {
        if (!IsValid(pfx, password))
            return null;
        var data = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["pfx"] = Convert.ToBase64String(pfx.Span),
            ["password"] = password,
        };
        if (publicCertificate is not null)
            data["public"] = publicCertificate;
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            return "ABOX-CERTIFICATE-1\n"
                + bytes.Length.ToString(CultureInfo.InvariantCulture)
                + "\n"
                + Convert.ToHexStringLower(SHA256.HashData(bytes))
                + "\n"
                + json;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static bool IsValid(ReadOnlyMemory<byte> pfx, string password) =>
        !pfx.IsEmpty
        && pfx.Length <= 1024 * 1024
        && !string.IsNullOrEmpty(password)
        && password.Length <= 1024
        && !password.Contains('\0');
}
