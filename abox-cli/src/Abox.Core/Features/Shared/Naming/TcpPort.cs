using System.Globalization;

namespace Abox.Features.Shared.Naming;

/// <summary>A parsed TCP port; callers retain their own diagnostics for invalid input.</summary>
public readonly record struct TcpPort
{
    private TcpPort(int value) => Value = value;

    public int Value { get; }

    public static bool IsValid(long value) => value is >= 1 and <= 65535;

    public static bool TryParse(ReadOnlySpan<char> text, out TcpPort port)
    {
        if (
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && IsValid(value)
        )
        {
            port = new TcpPort(value);
            return true;
        }
        port = default;
        return false;
    }
}
