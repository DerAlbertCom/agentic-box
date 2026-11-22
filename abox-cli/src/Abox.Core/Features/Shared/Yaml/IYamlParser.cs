namespace Abox.Features.Shared.Yaml;

/// <summary>
/// Turns YAML text into a <see cref="YamlNode"/> tree. Invalid YAML and a missing file are
/// <see cref="Common.AboxException"/>s that name the source.
/// </summary>
public interface IYamlParser
{
    /// <summary>Parses a YAML document. An empty document is an empty plain value.</summary>
    /// <param name="yaml">The document text.</param>
    /// <param name="source">Where the text comes from, for messages.</param>
    YamlNode Parse(string yaml, string source);

    /// <summary>Parses a YAML file, with its path as the source.</summary>
    YamlNode LoadFile(string path);
}
