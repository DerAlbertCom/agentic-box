namespace Abox.Features.Shared.Yaml;

/// <summary>A node of a parsed YAML document, with the file it comes from.</summary>
public abstract class YamlNode(string source)
{
    /// <summary>The file this node comes from, for messages.</summary>
    public string Source { get; } = source;
}

/// <summary>A plain or quoted value.</summary>
public sealed class YamlValue(string value, bool isPlain, string source) : YamlNode(source)
{
    public string Value { get; } = value;

    /// <summary>Whether the value is written without quotes, so it may be a number, boolean or null.</summary>
    public bool IsPlain { get; } = isPlain;
}

/// <summary>A list of nodes.</summary>
public sealed class YamlSequence(IReadOnlyList<YamlNode> items, string source) : YamlNode(source)
{
    public IReadOnlyList<YamlNode> Items { get; } = items;
}

/// <summary>A mapping of keys to nodes, in document order.</summary>
public sealed class YamlMapping(IReadOnlyList<YamlMappingEntry> entries, string source)
    : YamlNode(source)
{
    public IReadOnlyList<YamlMappingEntry> Entries { get; } = entries;
}

/// <summary>One key and its value in a <see cref="YamlMapping"/>.</summary>
public sealed record YamlMappingEntry(YamlNode Key, YamlNode Value);
