using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.Yaml;

/// <summary>
/// Reads the small YAML documents abox uses. The shapes are known, so the reader is
/// strict: an unknown key or a value of the wrong shape is an error, not a surprise.
/// </summary>
public sealed class YamlReader
{
    private readonly YamlMapping _mapping;
    private readonly YamlEntry _root;

    private YamlReader(YamlMapping root)
    {
        _mapping = root;
        _root = new YamlEntry(root, null);
    }

    /// <summary>Reads a parsed YAML document, which must be a mapping or empty.</summary>
    public static YamlReader Read(YamlNode root) =>
        root switch
        {
            YamlMapping mapping => new YamlReader(mapping),
            YamlValue { Value: "" } => new YamlReader(new YamlMapping([], root.Source)),
            _ => throw new AboxException(
                $"'{root.Source}' must contain a mapping at the top level."
            ),
        };

    /// <summary>
    /// Lays <paramref name="overlay"/> over <paramref name="base"/>: mappings are merged
    /// recursively, every other value (including lists) is replaced as a whole. An error
    /// about a value of the result names the file that supplied the value.
    /// </summary>
    public static YamlReader Merge(YamlReader @base, YamlReader overlay) =>
        new(Merge(@base._mapping, overlay._mapping));

    private static YamlMapping Merge(YamlMapping baseMapping, YamlMapping overlayMapping)
    {
        var merged = baseMapping.Entries.ToList();
        foreach (var entry in overlayMapping.Entries)
        {
            var index = merged.FindIndex(existing => YamlEntry.SameKey(existing.Key, entry.Key));
            var overlaid = entry.Value;
            if (
                entry.Value is YamlMapping overlayChild
                && index >= 0
                && merged[index].Value is YamlMapping baseChild
            )
            {
                overlaid = Merge(baseChild, overlayChild);
            }

            if (index >= 0)
            {
                merged.RemoveAt(index);
            }

            merged.Add(entry with { Value = overlaid });
        }

        return new YamlMapping(merged, overlayMapping.Source);
    }

    /// <summary>The file this reader reads from; for a merged document, the overlaid file.</summary>
    public string Source => _mapping.Source;

    /// <summary>The file that supplied the value of a key.</summary>
    public string SourceOf(string key) => _root.SourceOf(key);

    /// <summary>Whether the document has this key.</summary>
    public bool Has(string key) => _root.Has(key);

    /// <summary>The nested value of a key, or null when it is absent.</summary>
    public YamlEntry? GetEntry(string key) => _root.GetEntry(key);

    /// <summary>The value of a key, when it is a scalar.</summary>
    public string? GetString(string key) => _root.GetString(key);

    /// <summary>Whether the document has this key with a YAML null value.</summary>
    public bool IsNull(string key) => _root.IsNull(key);

    /// <summary>The value of a key, when it is a plain integer.</summary>
    public int? GetInteger(string key) => _root.GetInteger(key);

    /// <summary>The value of a key, when it is an unquoted boolean.</summary>
    public bool? GetBoolean(string key) => _root.GetBoolean(key);

    /// <summary>The value of a key, when it is a list of scalars.</summary>
    public IReadOnlyList<string> GetStringList(string key) => _root.GetStringList(key);

    /// <summary>The value of a key, when it is a mapping of scalars.</summary>
    public IReadOnlyDictionary<string, string> GetStringMap(string key) => _root.GetStringMap(key);

    /// <summary>The value of a key, when it is a list of scalars or mappings.</summary>
    public IReadOnlyList<YamlEntry> GetEntries(string key) => _root.GetEntries(key);

    /// <summary>Fails when the document has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known) => _root.RejectUnknownKeys(known);
}

/// <summary>Reads YAML through a parser into a strict <see cref="YamlReader"/>.</summary>
public static class YamlParserReading
{
    /// <summary>Reads a YAML document.</summary>
    public static YamlReader Read(this IYamlParser parser, string yaml, string source) =>
        YamlReader.Read(parser.Parse(yaml, source));

    /// <summary>Reads a YAML file.</summary>
    public static YamlReader ReadFile(this IYamlParser parser, string path) =>
        YamlReader.Read(parser.LoadFile(path));
}

/// <summary>
/// A YAML value or mapping, including the root of a document.
/// </summary>
public sealed class YamlEntry
{
    private readonly string? _key;

    internal YamlEntry(YamlNode node, string? key)
    {
        Node = node;
        _key = key;
    }

    private YamlNode Node { get; }

    /// <summary>The file this element comes from.</summary>
    public string Source => Node.Source;

    private string KeyPath(string key) => _key is null ? key : $"{_key}.{key}";

    private static string KeyName(YamlNode node) =>
        node is YamlValue scalar ? scalar.Value : node.ToString() ?? string.Empty;

    internal static bool SameKey(YamlNode left, YamlNode right) =>
        left is YamlValue leftValue && right is YamlValue rightValue
            ? leftValue.Value == rightValue.Value
            : ReferenceEquals(left, right);

    private bool TryGetValue(string key, out YamlNode node)
    {
        var entry = Mapping()
            .Entries.FirstOrDefault(entry =>
                entry.Key is YamlValue { Value: var name } && name == key
            );
        node = entry?.Value!;
        return entry is not null;
    }

    private static AboxException Invalid(YamlNode node, string key, string requirement) =>
        new($"'{node.Source}': '{key}' must be {requirement}.");

    /// <summary>The file that supplied the value of a key, or this element's file when it is absent.</summary>
    public string SourceOf(string key) => TryGetValue(key, out var node) ? node.Source : Source;

    /// <summary>Whether this mapping has a key.</summary>
    public bool Has(string key) => TryGetValue(key, out _);

    /// <summary>Whether the element is a plain value.</summary>
    public bool IsScalar => Node is YamlValue;

    /// <summary>The value of the element, when it is a plain value.</summary>
    public string ScalarValue => Node is YamlValue scalar ? scalar.Value : string.Empty;

    /// <summary>The value of a key of the element, when the element is a mapping.</summary>
    public string? GetString(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is not YamlValue scalar)
        {
            throw Invalid(node, KeyPath(key), "a value");
        }

        return scalar.Value;
    }

    /// <summary>Whether this mapping has a key with a YAML null value (empty, ~ or null, unquoted).</summary>
    public bool IsNull(string key) =>
        TryGetValue(key, out var node)
        && node is YamlValue { IsPlain: true } scalar
        && scalar.Value is "" or "~" or "null" or "Null" or "NULL";

    /// <summary>The value of a key of the element, when it is an unquoted integer.</summary>
    public int? GetInteger(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (
            node is not YamlValue { IsPlain: true } scalar
            || !int.TryParse(
                scalar.Value,
                System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value
            )
        )
        {
            throw Invalid(node, KeyPath(key), "an integer");
        }

        return value;
    }

    /// <summary>The value of a key of the element, when it is an unquoted <c>true</c> or <c>false</c>.</summary>
    public bool? GetBoolean(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is YamlValue { IsPlain: true } scalar)
        {
            switch (scalar.Value)
            {
                case "true" or "True" or "TRUE":
                    return true;
                case "false" or "False" or "FALSE":
                    return false;
            }
        }

        throw Invalid(node, KeyPath(key), "true or false");
    }

    /// <summary>The value of a key of the element, when it is a mapping of values.</summary>
    public IReadOnlyDictionary<string, string> GetStringMap(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return new Dictionary<string, string>();
        }

        if (node is not YamlMapping values)
        {
            throw Invalid(node, KeyPath(key), "a mapping");
        }

        var result = new Dictionary<string, string>();
        foreach (var (childKey, childValue) in values.Entries)
        {
            var name = KeyName(childKey);
            result[name] = Scalar(childValue, $"{KeyPath(key)}.{name}");
        }

        return result;
    }

    /// <summary>The value of a key of the element, when it is a list of values.</summary>
    public IReadOnlyList<string> GetStringList(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return [];
        }

        if (node is not YamlSequence sequence)
        {
            throw Invalid(node, KeyPath(key), "a list");
        }

        return sequence.Items.Select(child => Scalar(child, KeyPath(key))).ToList();
    }

    /// <summary>
    /// The value of a key of the element, as a list of values: a single value counts as
    /// a list of one.
    /// </summary>
    public IReadOnlyList<string> GetStrings(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return [];
        }

        if (node is YamlValue)
        {
            var value = Scalar(node, KeyPath(key));
            return value.Length == 0 ? [] : [value];
        }

        if (node is not YamlSequence sequence)
        {
            throw Invalid(node, KeyPath(key), "a value or a list");
        }

        return sequence.Items.Select(child => Scalar(child, KeyPath(key))).ToList();
    }

    private static string Scalar(YamlNode node, string key)
    {
        if (node is not YamlValue scalar)
        {
            throw Invalid(node, key, "a value");
        }

        return scalar.Value;
    }

    /// <summary>The value of a key of the element, when it is a mapping.</summary>
    public YamlEntry? GetEntry(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (_key is not null && node is not YamlMapping)
        {
            throw Invalid(node, KeyPath(key), "a mapping");
        }

        return new YamlEntry(node, KeyPath(key));
    }

    /// <summary>The value of a key of the element, when it is a list of mappings or values.</summary>
    public IReadOnlyList<YamlEntry> GetEntries(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return [];
        }

        if (node is not YamlSequence sequence)
        {
            throw Invalid(node, KeyPath(key), "a list");
        }

        return sequence.Items.Select(child => new YamlEntry(child, KeyPath(key))).ToList();
    }

    /// <summary>Fails when the element has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known)
    {
        foreach (var (childKey, _) in Mapping().Entries)
        {
            var name = KeyName(childKey);
            if (!known.Contains(name))
            {
                throw new AboxException(
                    $"'{childKey.Source}': '{KeyPath(name)}' is not a known key. Known keys: {string.Join(", ", known)}."
                );
            }
        }
    }

    private YamlMapping Mapping()
    {
        if (Node is not YamlMapping mapping)
        {
            throw new AboxException(
                $"'{Source}': every '{_key}' entry must be a value or a mapping."
            );
        }

        return mapping;
    }
}
