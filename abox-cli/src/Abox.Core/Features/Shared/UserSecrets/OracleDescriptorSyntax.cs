using System.Text.RegularExpressions;

namespace Abox.Features.Shared.UserSecrets;

// Bounded structural syntax retains partial trees and original leaf spans. A
// later syntax error must not erase already-known mapped ADDRESS candidates.
internal sealed partial class OracleDescriptorSyntax(string value)
{
    private int _position;
    private bool _valid = true;
    internal bool Complete { get; private set; }

    internal static bool Recognizes(string value) => OracleDescriptorPrefix().IsMatch(value);

    internal Node? Parse()
    {
        var root = ReadNode(0);
        Whitespace();
        Complete = _valid && _position == value.Length;
        return root;
    }

    private Node? ReadNode(int depth)
    {
        Whitespace();
        if (depth > 64 || !Take('('))
        {
            _valid = false;
            return null;
        }
        var key = ReadKey();
        if (key.Length == 0 || !Take('='))
        {
            _valid = false;
            return null;
        }
        var node = new Node(key);
        Whitespace();
        if (_position < value.Length && value[_position] == '(')
            ReadChildren(node, depth);
        else
            ReadLeaf(node);
        Whitespace();
        _valid &= Take(')');
        return node;
    }

    private string ReadKey()
    {
        Whitespace();
        var start = _position;
        while (
            _position < value.Length
            && (char.IsAsciiLetterOrDigit(value[_position]) || value[_position] is '_' or '.')
        )
            _position++;
        var key = value[start.._position].ToUpperInvariant();
        Whitespace();
        return key;
    }

    private void ReadChildren(Node node, int depth)
    {
        while (_valid && _position < value.Length && value[_position] == '(')
        {
            var child = ReadNode(depth + 1);
            if (child is not null)
                node.Children.Add(child);
            Whitespace();
        }
    }

    private void ReadLeaf(Node node)
    {
        if (_position < value.Length && value[_position] is '\'' or '"')
        {
            ReadQuotedLeaf(node, value[_position++]);
            return;
        }
        node.Start = _position;
        while (_position < value.Length && value[_position] != ')')
        {
            if (value[_position] is '(' or '\'' or '"')
            {
                _valid = false;
                break;
            }
            _position++;
        }
        node.Length = _position - node.Start;
        while (node.Length > 0 && char.IsWhiteSpace(value[node.Start + node.Length - 1]))
            node.Length--;
    }

    private void ReadQuotedLeaf(Node node, char quote)
    {
        node.Start = _position;
        while (_position < value.Length && value[_position] != quote)
        {
            if (value[_position] == '\\' && _position + 1 < value.Length)
                _position++;
            _position++;
        }
        node.Length = _position - node.Start;
        _valid &= Take(quote);
    }

    private bool Take(char character)
    {
        if (_position == value.Length || value[_position] != character)
            return false;
        _position++;
        return true;
    }

    private void Whitespace()
    {
        while (_position < value.Length && char.IsWhiteSpace(value[_position]))
            _position++;
    }

    internal sealed class Node(string key)
    {
        internal string Key { get; } = key;
        internal int Start { get; set; }
        internal int Length { get; set; }
        internal List<Node> Children { get; } = [];
    }

    [GeneratedRegex(
        @"\A\s*\(\s*(?:DESCRIPTION(?:_LIST)?|ADDRESS(?:_LIST)?)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OracleDescriptorPrefix();
}
