using Abox.Features.Shared.Common;
using Abox.Features.Shared.Yaml;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlNode = Abox.Features.Shared.Yaml.YamlNode;

namespace Abox.Infrastructure;

/// <summary><see cref="IYamlParser"/> on top of YamlDotNet.</summary>
internal sealed class YamlDotNetParser : IYamlParser
{
    public YamlNode LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new AboxException($"'{path}' does not exist.");
        }

        return Parse(File.ReadAllText(path), path);
    }

    public YamlNode Parse(string yaml, string source)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException exception)
        {
            throw new AboxException(
                $"'{source}' is not valid YAML: {exception.Message}",
                exception
            );
        }

        return stream.Documents.Count > 0
            ? Convert(stream.Documents[0].RootNode, source)
            : new YamlValue(string.Empty, isPlain: true, source);
    }

    private static YamlNode Convert(YamlDotNet.RepresentationModel.YamlNode node, string source) =>
        node switch
        {
            YamlMappingNode mapping => new YamlMapping(
                mapping
                    .Children.Select(child => new YamlMappingEntry(
                        Convert(child.Key, source),
                        Convert(child.Value, source)
                    ))
                    .ToList(),
                source
            ),
            YamlSequenceNode sequence => new YamlSequence(
                sequence.Children.Select(child => Convert(child, source)).ToList(),
                source
            ),
            YamlScalarNode scalar => new YamlValue(
                scalar.Value ?? string.Empty,
                scalar.Style is ScalarStyle.Plain or ScalarStyle.Any,
                source
            ),
            _ => throw new NotSupportedException(
                $"YAML node type {node.NodeType} is not supported."
            ),
        };
}
