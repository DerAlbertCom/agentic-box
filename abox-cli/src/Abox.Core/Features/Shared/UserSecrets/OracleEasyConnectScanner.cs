using System.Text.RegularExpressions;
using Abox.Features.Shared.Naming;
using static Abox.Features.Shared.UserSecrets.EndpointSyntax;

namespace Abox.Features.Shared.UserSecrets;

internal sealed partial class OracleEasyConnectScanner(string value, HashSet<int> ports)
{
    private readonly string _text = value.Trim();
    private readonly int _leading = value.Length - value.TrimStart().Length;
    private readonly List<EndpointSourceSpan> _hosts = [];
    private int _position;
    private bool _valid;
    private bool _recognized;

    internal static OracleHostScan Scan(
        string value,
        HashSet<int> ports,
        bool inConnectionString
    ) => new OracleEasyConnectScanner(value, ports).Read(inConnectionString);

    private OracleHostScan Read(bool inConnectionString)
    {
        var prefix = OracleEasyPrefix().Match(_text);
        var end = _text.IndexOfAny(['/', '?'], prefix.Length);
        if (end < 0)
            end = _text.Length;
        var authority = _text[prefix.Length..end];
        var suffix = _text[end..];
        if (
            !Recognizes(prefix, authority, inConnectionString)
            || IsGenericUri(prefix, authority, suffix)
        )
            return new(false, true, []);
        _valid =
            !_text.Any(char.IsControl)
            && OracleEasySuffix().IsMatch(suffix)
            && OracleEasyConnectOptions.Valid(suffix);
        _position = prefix.Length;
        foreach (var list in authority.Split(';'))
            ReadList(list);
        return new(_recognized, _valid, _hosts);
    }

    private bool Recognizes(Match prefix, string authority, bool inConnectionString)
    {
        if (!prefix.Success)
        {
            // SQL Server's tcp:host,port and other schemes retain their dispatchers.
            if (
                _text.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)
                || _text.Contains("://", StringComparison.Ordinal)
            )
                return false;
            if (!inConnectionString && authority.Any(char.IsWhiteSpace))
                return false;
            if (authority.Length == _text.Length && !authority.Contains(':'))
                return false; // A bare name can be a TNS alias.
        }
        return !authority.Contains('=') && !authority.Contains('(');
    }

    private bool IsGenericUri(Match prefix, string authority, string suffix) =>
        // URI-only userinfo, paths and fragments predate Oracle support.
        prefix.Success
        && !_text.StartsWith("//", StringComparison.Ordinal)
        && !authority.Contains(',')
        && !authority.Contains(';')
        && (authority.Contains('@') || !OracleEasySuffix().IsMatch(suffix))
        && Uri.TryCreate(_text, UriKind.Absolute, out var uri)
        && uri.IsWellFormedOriginalString();

    private void ReadList(string list)
    {
        var pending = new List<EndpointSourceSpan>();
        foreach (var address in list.Split(','))
        {
            ReadAddress(address, pending);
            _position += address.Length + 1;
        }
        if (ports.Contains(1521))
            _hosts.AddRange(pending);
    }

    private void ReadAddress(string address, List<EndpointSourceSpan> pending)
    {
        if (ReadAmbiguousIpv6(address))
            return;
        var match = OracleEasyAddress().Match(address);
        if (!match.Success)
        {
            _valid = false;
            return;
        }
        _recognized = true;
        _valid &= match.Length == address.Length;
        var host = match.Groups["host"];
        if (IsLoopback(host.Value))
            pending.Add(new(_leading + _position + host.Index, host.Length));
        var port = match.Groups["port"];
        if (!port.Success)
            return;
        if (TcpPort.TryParse(port.Value, out var number))
        {
            if (ports.Contains(number.Value))
                _hosts.AddRange(pending);
        }
        else
            _valid = false;
        pending.Clear();
    }

    private bool ReadAmbiguousIpv6(string address)
    {
        var match = OracleUnbracketedIpv6().Match(address);
        var bareLoopback = !address.StartsWith('[') && address.Contains(':') && IsLoopback(address);
        if (!match.Success && !bareLoopback)
            return false;
        _recognized = true;
        _valid = false;
        var port = match.Success ? match.Groups["port"].Value : "1521";
        if (IsMappedEndpointPort(port, ports))
            _hosts.Add(new(_leading + _position, address.Length));
        return true;
    }

    [GeneratedRegex(@"\A(?:tcps?:)?//", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OracleEasyPrefix();

    [GeneratedRegex(
        @"\A(?<host>\[[^\]\s]+\]|[a-z0-9._-]+)(?::(?<port>[0-9]+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleEasyAddress();

    [GeneratedRegex(
        @"\A(?:\:\:1|0:0:0:0:0:0:0:1):(?<port>[0-9]+)\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleUnbracketedIpv6();

    [GeneratedRegex(
        @"\A(?:/(?:[a-z0-9._$-]*)(?::(?:dedicated|shared|pooled))?(?:/[a-z0-9._$-]+)?)?(?:\?[^\r\n#]*)?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleEasySuffix();
}
