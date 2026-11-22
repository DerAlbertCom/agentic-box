namespace Abox.Features.Shared.UserSecrets;

internal sealed class OracleEasyConnectOptions(string suffix)
{
    private int _position;

    internal static bool Valid(string suffix)
    {
        var question = suffix.IndexOf('?');
        return question < 0
            || new OracleEasyConnectOptions(suffix) { _position = question + 1 }.Read();
    }

    private bool Read()
    {
        while (_position < suffix.Length)
        {
            if (!ReadKey() || !ReadValue())
                return false;
            if (
                _position < suffix.Length
                && (suffix[_position++] != '&' || _position == suffix.Length)
            )
                return false;
        }
        return true;
    }

    private bool ReadKey()
    {
        var start = _position;
        while (
            _position < suffix.Length
            && (char.IsAsciiLetterOrDigit(suffix[_position]) || suffix[_position] is '_' or '.')
        )
            _position++;
        var key = suffix[start.._position].ToUpperInvariant();
        return key.Length > 0
            && _position < suffix.Length
            && suffix[_position++] == '='
            && key
                is not (
                    "HOST"
                    or "PORT"
                    or "PROTOCOL"
                    or "ADDRESS"
                    or "ADDRESS_LIST"
                    or "DESCRIPTION"
                    or "DESCRIPTION_LIST"
                    or "HTTPS_PROXY"
                    or "HTTPS_PROXY_PORT"
                );
    }

    private bool ReadValue()
    {
        if (_position < suffix.Length && suffix[_position] is '\'' or '"')
            return ReadQuotedValue(suffix[_position++]);
        while (_position < suffix.Length)
        {
            var character = suffix[_position++];
            if (character == '&')
            {
                _position--;
                return true;
            }
            else if (char.IsWhiteSpace(character) || character is '\'' or '"' or '(' or ')' or '\\')
                return false;
        }
        return true;
    }

    private bool ReadQuotedValue(char quote)
    {
        while (_position < suffix.Length)
        {
            var character = suffix[_position++];
            if (character == '\\' && _position < suffix.Length)
                _position++;
            else if (character == quote)
                return true;
        }
        return false;
    }
}
