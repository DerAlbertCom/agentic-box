namespace Abox.Features.Shared.UserSecrets;

// Value spans exclude outer quotes and unquoted trailing whitespace. Doubled quote
// escapes remain encoded in the original source; the scanner does not decode values.
internal sealed record ConnectionField(string Key, int Start, int Length);

// Complete fields remain usable for fail-closed endpoint recognition after a later error.
// PartialField retains a known key and the attempted value prefix up to StoppedAt,
// not a successfully parsed field (an invalid delimiter can leave a closing quote
// in this prefix). Unique covers detected keys, including that partial field.
// Consumers must opt into inspecting it, never search inside another field's value,
// and never treat Complete=false as an empty connection string. StoppedAt is the
// parser cursor, not the next field's start; no missing syntax is inferred.
// These outcomes describe parsing, not mandatory recovery behavior: Oracle needs
// its bounded original remainder rather than the attempted prefix for recognition.
internal sealed record ConnectionStringScan(
    bool Complete,
    bool Unique,
    IReadOnlyList<ConnectionField> Fields,
    ConnectionField? PartialField,
    int StoppedAt
)
{
    internal bool CompleteAndUnique => Complete && Unique;
}

internal sealed class ConnectionStringScanner(string value)
{
    private int _position;
    private ConnectionField? _partial;

    internal static ConnectionStringScan Scan(string value) =>
        new ConnectionStringScanner(value).Read();

    private ConnectionStringScan Read()
    {
        var fields = new List<ConnectionField>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = true;
        while (_position < value.Length)
        {
            SkipSeparators();
            if (_position == value.Length)
                break;
            var field = ReadField();
            if (field is null)
            {
                unique &= _partial is null || keys.Add(_partial.Key);
                return new(false, unique, fields, _partial, _position);
            }
            unique &= keys.Add(field.Key);
            fields.Add(field);
        }
        return new(true, unique, fields, null, _position);
    }

    private ConnectionField? ReadField()
    {
        _partial = null;
        var key = ReadKey();
        if (key is null)
            return null;
        _position++;
        SkipWhitespace();
        var start = _position;
        var end = ReadValue(ref start);
        if (end is null)
        {
            _partial = new(key, start, _position - start);
            return null;
        }
        return new(key, start, end.Value - start);
    }

    private string? ReadKey()
    {
        var start = _position;
        while (_position < value.Length && value[_position] != '=')
        {
            var character = value[_position++];
            if (!(char.IsAsciiLetterOrDigit(character) || character is ' ' or '_' or '-'))
                return null;
        }
        if (_position == value.Length)
            return null;
        var key = value[start.._position].Trim();
        return key.Length == 0 ? null : key;
    }

    private int? ReadValue(ref int start)
    {
        if (_position < value.Length && value[_position] is '\'' or '"')
        {
            var quote = value[_position++];
            start = _position;
            return ReadQuotedValue(quote);
        }
        while (_position < value.Length && value[_position] != ';')
        {
            if (value[_position] is '\'' or '"' or '{' or '}')
                return null;
            _position++;
        }
        var end = _position;
        while (end > start && char.IsWhiteSpace(value[end - 1]))
            end--;
        return end;
    }

    private int? ReadQuotedValue(char quote)
    {
        while (_position < value.Length)
        {
            if (value[_position] != quote)
            {
                _position++;
                continue;
            }
            if (_position + 1 < value.Length && value[_position + 1] == quote)
            {
                _position += 2;
                continue;
            }
            var end = _position++;
            SkipWhitespace();
            return _position < value.Length && value[_position] != ';' ? null : end;
        }
        return null;
    }

    private void SkipWhitespace()
    {
        while (_position < value.Length && char.IsWhiteSpace(value[_position]))
            _position++;
    }

    private void SkipSeparators()
    {
        while (
            _position < value.Length
            && (char.IsWhiteSpace(value[_position]) || value[_position] == ';')
        )
            _position++;
    }
}
