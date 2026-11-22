using Abox.Infrastructure;

namespace Abox.Features.Shared.Squads;

/// <summary>
/// The descriptor of a kit: what the kit needs from the host.
/// </summary>
public sealed class KitDescriptor
{
    /// <summary>The capability a kit uses to ask for a credential.</summary>
    public const string CredentialCapability = "com.docker.sandbox/credential@1";

    private KitDescriptor(string descriptorPath, IReadOnlyList<KitService> services)
    {
        DescriptorPath = descriptorPath;
        Services = services;
    }

    /// <summary>The descriptor file, or where the descriptor text comes from.</summary>
    public string DescriptorPath { get; }

    /// <summary>The services the kit authenticates to.</summary>
    public IReadOnlyList<KitService> Services { get; }

    /// <summary>
    /// Reads the descriptor of a kit directory. Returns null when the directory has no
    /// descriptor.
    /// </summary>
    public static KitDescriptor? Load(string kitDirectory)
    {
        var path = FindDescriptor(kitDirectory);
        if (path is null)
        {
            return null;
        }

        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Reads the text of a descriptor, such as one the kit frontend staged in a sandbox.
    /// </summary>
    /// <param name="yaml">The descriptor text.</param>
    /// <param name="source">Where the text comes from, for messages.</param>
    public static KitDescriptor Parse(string yaml, string source)
    {
        var reader = YamlReader.Parse(yaml, source);
        var services = new List<KitService>();

        foreach (var capability in reader.GetEntries("capabilities"))
        {
            if (capability.GetString("type") != CredentialCapability)
            {
                continue;
            }

            var config = capability.GetEntry("config");
            var service = config?.GetString("service");
            if (string.IsNullOrWhiteSpace(service))
            {
                continue;
            }

            var domains =
                config!
                    .GetEntry("apiKey")
                    ?.GetEntries("inject")
                    .Select(inject => inject.GetString("domain"))
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Select(domain => domain!)
                    .ToList()
                ?? [];
            var optional = string.Equals(
                capability.GetString("optional"),
                "true",
                StringComparison.OrdinalIgnoreCase
            );

            services.Add(new KitService(service, domains, optional));
        }

        return new KitDescriptor(source, services);
    }

    private static string? FindDescriptor(string kitDirectory)
    {
        if (!Directory.Exists(kitDirectory))
        {
            return null;
        }

        var conventional = Path.Combine(kitDirectory, Path.GetFileName(kitDirectory) + ".yaml");
        if (File.Exists(conventional))
        {
            return conventional;
        }

        var spec = Path.Combine(kitDirectory, "spec.yaml");
        if (File.Exists(spec))
        {
            return spec;
        }

        return Directory
            .EnumerateFiles(kitDirectory, "*.yaml")
            .OrderBy(file => file)
            .FirstOrDefault();
    }
}

/// <summary>
/// A service a kit authenticates to. The kits declare the services they need with the
/// <c>credential@1</c> capability; abox keeps the secrets of those services on the host.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
/// <param name="Domains">The domains the API key is sent to; empty without an <c>apiKey</c> mechanism.</param>
/// <param name="Optional">Whether the sandbox starts without the credential.</param>
public sealed record KitService(
    string Service,
    IReadOnlyList<string> Domains,
    bool Optional = false
);
