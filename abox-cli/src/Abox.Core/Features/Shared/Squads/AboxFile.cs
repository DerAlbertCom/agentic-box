using Abox.Features.Shared.Common;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Squads;

/// <summary>
/// A parsed <c>.abox</c> file: the configuration of a squad, or of a base it inherits from.
/// </summary>
internal sealed class AboxFile
{
    private AboxFile(
        string path,
        string? @base,
        string? workload,
        IReadOnlyList<KitDeclaration> kits,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<McpDeclaration> mcp,
        IReadOnlyList<string> state
    )
    {
        Path = path;
        Base = @base;
        Workload = workload;
        Kits = kits;
        Parameters = parameters;
        Mcp = mcp;
        State = state;
    }

    /// <summary>The file this configuration was read from.</summary>
    public string Path { get; }

    /// <summary>The directory the file lives in. Kits and workloads are resolved against it.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? ".";

    /// <summary>The name of the configuration this one inherits from.</summary>
    public string? Base { get; }

    /// <summary>The name of the workload, which only the root configuration declares.</summary>
    public string? Workload { get; }

    /// <summary>The kits this configuration adds.</summary>
    public IReadOnlyList<KitDeclaration> Kits { get; }

    /// <summary>The named values this configuration declares.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>The MCP servers this configuration adds.</summary>
    public IReadOnlyList<McpDeclaration> Mcp { get; }

    /// <summary>The sandbox directories this configuration keeps across a replacement.</summary>
    public IReadOnlyList<string> State { get; }

    /// <summary>Reads a <c>.abox</c> file.</summary>
    public static AboxFile Load(string path, IYamlParser parser)
    {
        var reader = parser.ReadFile(path);
        if (reader.Has("secrets"))
        {
            throw new AboxException(
                $"'{path}': a squad configuration no longer declares 'secrets'. "
                    + $"Move each entry into the '{KitSecrets.FileName}' next to the descriptor of the kit that asks for the service; "
                    + "a custom secret goes to the kit whose setup reads it (see docs/authoring-secrets.md)."
            );
        }

        reader.RejectUnknownKeys("base", "workload", "kits", "parameters", "mcp", "state");

        return new AboxFile(
            path,
            reader.GetString("base"),
            reader.GetString("workload"),
            ReadKits(reader),
            reader.GetStringMap("parameters"),
            ReadMcp(reader),
            reader.GetStringList("state")
        );
    }

    private static IReadOnlyList<KitDeclaration> ReadKits(YamlReader reader)
    {
        var kits = new List<KitDeclaration>();

        foreach (var entry in reader.GetEntries("kits"))
        {
            if (entry.IsScalar)
            {
                kits.Add(new KitDeclaration(entry.ScalarValue, new Dictionary<string, string>()));
                continue;
            }

            entry.RejectUnknownKeys("name", "args");
            var name =
                entry.GetString("name")
                ?? throw new AboxException($"'{reader.Source}': a kit entry needs a 'name'.");
            kits.Add(new KitDeclaration(name, entry.GetStringMap("args")));
        }

        return kits;
    }

    private static IReadOnlyList<McpDeclaration> ReadMcp(YamlReader reader)
    {
        var servers = new List<McpDeclaration>();

        foreach (var entry in reader.GetEntries("mcp"))
        {
            if (entry.IsScalar)
            {
                servers.Add(new McpDeclaration(entry.ScalarValue));
                continue;
            }

            entry.RejectUnknownKeys("name", "url");
            var name =
                entry.GetString("name")
                ?? throw new AboxException($"'{reader.Source}': an mcp entry needs a 'name'.");

            servers.Add(new McpDeclaration(name, entry.GetString("url")));
        }

        return servers;
    }
}
