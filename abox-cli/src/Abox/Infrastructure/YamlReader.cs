using Abox.Features.Shared.Common;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Abox.Infrastructure;

/// <summary>
/// Reads the small YAML documents abox uses. The shapes are known, so the reader is
/// strict: an unknown key or a value of the wrong shape is an error, not a surprise.
/// </summary>
public sealed class YamlReader
{
    private readonly YamlMappingNode _mapping;
    private readonly YamlEntry _root;
    private readonly string _source;
    private readonly YamlSources _sources;

    private YamlReader(YamlMappingNode root, string source, YamlSources sources)
    {
        _mapping = root;
        _sources = sources;
        _root = new YamlEntry(root, source, null, sources);
        _source = source;
    }

    /// <summary>Reads a YAML file.</summary>
    public static YamlReader LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new AboxException($"'{path}' does not exist.");
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Reads a YAML document.</summary>
    public static YamlReader Parse(string yaml, string source)
    {
        YamlNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            root =
                stream.Documents.Count > 0
                    ? stream.Documents[0].RootNode
                    : new YamlScalarNode(string.Empty);
        }
        catch (YamlException exception)
        {
            throw new AboxException(
                $"'{source}' is not valid YAML: {exception.Message}",
                exception
            );
        }

        if (root is not YamlMappingNode mapping)
        {
            if (root is YamlScalarNode { Value: "" or null })
            {
                return new YamlReader(new YamlMappingNode(), source, new YamlSources());
            }

            throw new AboxException($"'{source}' must contain a mapping at the top level.");
        }

        return new YamlReader(mapping, source, new YamlSources());
    }

    /// <summary>
    /// Lays <paramref name="overlay"/> over <paramref name="base"/>: mappings are merged
    /// recursively, every other value (including lists) is replaced as a whole. An error
    /// about a value of the result names the file that supplied the value.
    /// </summary>
    public static YamlReader Merge(YamlReader @base, YamlReader overlay)
    {
        var sources = new YamlSources();
        var root = Merge(@base, @base._mapping, overlay, overlay._mapping, sources);
        return new YamlReader(root, overlay._source, sources);
    }

    private static YamlMappingNode Merge(
        YamlReader @base,
        YamlMappingNode baseMapping,
        YamlReader overlay,
        YamlMappingNode overlayMapping,
        YamlSources sources
    )
    {
        var merged = new YamlMappingNode();
        foreach (var (key, value) in baseMapping.Children)
        {
            @base.Track(key, sources);
            @base.Track(value, sources);
            merged.Add(key, value);
        }

        foreach (var (key, value) in overlayMapping.Children)
        {
            var overlaid = value;
            if (
                value is YamlMappingNode overlayChild
                && merged.Children.TryGetValue(key, out var baseValue)
                && baseValue is YamlMappingNode baseChild
            )
            {
                overlaid = Merge(@base, baseChild, overlay, overlayChild, sources);
                sources.Add(overlaid, overlay.SourceOf(value));
            }
            else
            {
                overlay.Track(value, sources);
            }

            overlay.Track(key, sources);
            merged.Children.Remove(key);
            merged.Add(key, overlaid);
        }

        return merged;
    }

    private void Track(YamlNode node, YamlSources sources)
    {
        foreach (var child in node.AllNodes)
        {
            sources.Add(child, SourceOf(child));
        }
    }

    private string SourceOf(YamlNode node) => _sources.Of(node) ?? _source;

    /// <summary>The file this reader reads from; for a merged document, the overlaid file.</summary>
    public string Source => _source;

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

/// <summary>The file each node of a merged document came from.</summary>
internal sealed class YamlSources
{
    private readonly Dictionary<YamlNode, string> _files = new(ReferenceEqualityComparer.Instance);

    public void Add(YamlNode node, string file) => _files[node] = file;

    public string? Of(YamlNode node) => _files.GetValueOrDefault(node);
}

/// <summary>
/// A YAML value or mapping, including the root of a document.
/// </summary>
public sealed class YamlEntry
{
    private readonly string _source;
    private readonly string? _key;
    private readonly YamlSources _sources;

    internal YamlEntry(YamlNode node, string source, string? key, YamlSources sources)
    {
        Node = node;
        _source = source;
        _key = key;
        _sources = sources;
    }

    private YamlNode Node { get; }

    /// <summary>The file this element comes from.</summary>
    public string Source => _source;

    private string KeyPath(string key) => _key is null ? key : $"{_key}.{key}";

    private static string KeyName(YamlNode node) =>
        node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : node.ToString();

    private string SourceOf(YamlNode node) => _sources.Of(node) ?? _source;

    private bool TryGetValue(string key, out YamlNode node) =>
        Mapping().Children.TryGetValue(new YamlScalarNode(key), out node!);

    private AboxException Invalid(YamlNode node, string key, string requirement) =>
        new($"'{SourceOf(node)}': '{key}' must be {requirement}.");

    /// <summary>The file that supplied the value of a key, or this element's file when it is absent.</summary>
    public string SourceOf(string key) => TryGetValue(key, out var node) ? SourceOf(node) : _source;

    /// <summary>Whether this mapping has a key.</summary>
    public bool Has(string key) => TryGetValue(key, out _);

    /// <summary>Whether the element is a plain value.</summary>
    public bool IsScalar => Node is YamlScalarNode;

    /// <summary>The value of the element, when it is a plain value.</summary>
    public string ScalarValue =>
        Node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty;

    /// <summary>The value of a key of the element, when the element is a mapping.</summary>
    public string? GetString(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (node is not YamlScalarNode scalar)
        {
            throw Invalid(node, KeyPath(key), "a value");
        }

        return scalar.Value;
    }

    /// <summary>Whether this mapping has a key with a YAML null value (empty, ~ or null, unquoted).</summary>
    public bool IsNull(string key) =>
        TryGetValue(key, out var node)
        && node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar
        && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL";

    /// <summary>The value of a key of the element, when it is an unquoted integer.</summary>
    public int? GetInteger(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (
            node is not YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar
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

        if (node is YamlScalarNode { Style: ScalarStyle.Plain or ScalarStyle.Any } scalar)
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

        if (node is not YamlMappingNode values)
        {
            throw Invalid(node, KeyPath(key), "a mapping");
        }

        var result = new Dictionary<string, string>();
        foreach (var (childKey, childValue) in values.Children)
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

        if (node is not YamlSequenceNode sequence)
        {
            throw Invalid(node, KeyPath(key), "a list");
        }

        return sequence.Children.Select(child => Scalar(child, KeyPath(key))).ToList();
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

        if (node is YamlScalarNode)
        {
            var value = Scalar(node, KeyPath(key));
            return value.Length == 0 ? [] : [value];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw Invalid(node, KeyPath(key), "a value or a list");
        }

        return sequence.Children.Select(child => Scalar(child, KeyPath(key))).ToList();
    }

    private string Scalar(YamlNode node, string key)
    {
        if (node is not YamlScalarNode scalar)
        {
            throw Invalid(node, key, "a value");
        }

        return scalar.Value ?? string.Empty;
    }

    /// <summary>The value of a key of the element, when it is a mapping.</summary>
    public YamlEntry? GetEntry(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return null;
        }

        if (_key is not null && node is not YamlMappingNode)
        {
            throw Invalid(node, KeyPath(key), "a mapping");
        }

        return new YamlEntry(node, SourceOf(node), KeyPath(key), _sources);
    }

    /// <summary>The value of a key of the element, when it is a list of mappings or values.</summary>
    public IReadOnlyList<YamlEntry> GetEntries(string key)
    {
        if (!TryGetValue(key, out var node))
        {
            return [];
        }

        if (node is not YamlSequenceNode sequence)
        {
            throw Invalid(node, KeyPath(key), "a list");
        }

        return sequence
            .Children.Select(child => new YamlEntry(child, SourceOf(child), KeyPath(key), _sources))
            .ToList();
    }

    /// <summary>Fails when the element has keys abox does not know.</summary>
    public void RejectUnknownKeys(params string[] known)
    {
        foreach (var (childKey, _) in Mapping().Children)
        {
            var name = KeyName(childKey);
            if (!known.Contains(name))
            {
                throw new AboxException(
                    $"'{SourceOf(childKey)}': '{KeyPath(name)}' is not a known key. Known keys: {string.Join(", ", known)}."
                );
            }
        }
    }

    private YamlMappingNode Mapping()
    {
        if (Node is not YamlMappingNode mapping)
        {
            throw new AboxException(
                $"'{_source}': every '{_key}' entry must be a value or a mapping."
            );
        }

        return mapping;
    }
}
