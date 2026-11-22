using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.Features.Shared.Registry;

/// <summary>Digest and repository rules of OCI image references.</summary>
public static class ImageReferences
{
    private static readonly Regex DigestPattern = new(@"\Asha256:[a-f0-9]{64}\z");

    public static bool IsDigest(string? value) => value is not null && DigestPattern.IsMatch(value);

    public static string Digest(string? value) =>
        IsDigest(value) ? value! : throw new JsonException("Invalid published digest.");

    public static string Pin(string reference, string digest) =>
        reference[..reference.LastIndexOf(':')] + "@" + digest;

    public static string Repository(string reference)
    {
        var repository = reference.Split('@')[0];
        var colon = repository.LastIndexOf(':');
        return colon > repository.LastIndexOf('/') ? repository[..colon] : repository;
    }
}
