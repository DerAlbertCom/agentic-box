using System.Text.Json.Serialization;

namespace Abox.Features.Publish;

/// <summary>A component in composition order. Only supplied kits carry args, even when empty.</summary>
internal sealed record PublicationComponent(
    [property: JsonPropertyName("ref")] string Reference,
    [property: JsonPropertyName("args")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        IReadOnlyDictionary<string, string>? Arguments = null
);

/// <summary>The v3 set input; the frontend publishes it as a processed workload.</summary>
internal sealed record PublicationSetDescriptor
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion => "3";

    [JsonPropertyName("kind")]
    public string Kind => "set";

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("kits")]
    public required IReadOnlyList<PublicationComponent> Kits { get; init; }
}
