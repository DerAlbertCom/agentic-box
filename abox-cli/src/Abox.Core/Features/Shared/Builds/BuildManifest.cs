using System.Text.Json.Serialization;
using Abox.Features.Shared.Registry;

namespace Abox.Features.Shared.Builds;

/// <summary>
/// The build record of a local build: the component images <c>abox build</c> made, with
/// their digests and input hashes, and the registry kits and base image pinned by digest.
/// </summary>
internal sealed class BuildManifest
{
    /// <summary>The squad the build belongs to.</summary>
    public string Squad { get; set; } = string.Empty;

    /// <summary>The tag the build is stored under.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>When the build was made.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The owning project root discovered with the selected team.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>The name of the workload the base image is built from.</summary>
    public string WorkloadName { get; set; } = string.Empty;

    /// <summary>The JFrog repository selected by the resolved workload.</summary>
    public string? PublishRepository { get; set; }

    /// <summary>The directory of the generated workload kit.</summary>
    public string WorkloadKitDirectory { get; set; } = string.Empty;

    /// <summary>The directory of the generated mixin that declares the state directories.</summary>
    public string? StateMixinDirectory { get; set; }

    /// <summary>The directory of the generated mixin that bundles squad.json; null on older manifests.</summary>
    public string? ConfigurationMixinDirectory { get; set; }

    /// <summary>The image the workload builds on.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>The kits of the squad.</summary>
    public List<ManifestKit> Kits { get; set; } = [];

    /// <summary>The MCP servers of the squad.</summary>
    public List<ManifestMcp> Mcp { get; set; } = [];

    /// <summary>The directories the squad keeps across a replacement.</summary>
    public List<ManifestState> State { get; set; } = [];

    /// <summary>The configuration files the squad is made of.</summary>
    public List<string> ConfigFiles { get; set; } = [];

    /// <summary>The unqualified team ownership marker; remote publication adds a squad path segment.</summary>
    public string? Reference { get; set; }

    /// <summary>The host image store the component images are in; null on older builds.</summary>
    public string? ImageStore { get; set; }

    /// <summary>The base image (the workload's template) pinned by digest at build time; null on older builds.</summary>
    public PinnedImage? Base { get; set; }

    /// <summary>The component images, in set order: workload, local kits, state, config.</summary>
    public List<BuildComponent> Components { get; set; } = [];

    /// <summary>The component image of the given name, or null when the build has none.</summary>
    public BuildComponent? Component(string name) =>
        Components.FirstOrDefault(component => component.Name == name);

    /// <summary>SHA-256 fingerprints of the local source directories and configuration files.</summary>
    public Dictionary<string, string> SourceHashes { get; set; } = [];
}

/// <summary>A registry reference as written, and the digest it resolved to at build time.</summary>
internal sealed class PinnedImage
{
    public string Reference { get; set; } = string.Empty;
    public string Digest { get; set; } = string.Empty;
}

/// <summary>The kind of a component image, as its kit descriptor declares it.</summary>
internal static class ComponentKind
{
    public const string Workload = "workload";
    public const string Mixin = "mixin";
}

/// <summary>The names of the components every build has, under the squad's <c>components</c> repository.</summary>
internal static class ComponentNames
{
    public const string Workload = "workload";
    public const string State = "state";
    public const string Configuration = "config";

    /// <summary>The component name of a local kit.</summary>
    public static string Kit(string kit) => "kits/" + kit.ToLowerInvariant();
}

/// <summary>A component image of a local build, named with its final registry name.</summary>
internal sealed class BuildComponent
{
    /// <summary>The component's name under the squad's <c>components</c> repository, such as <c>workload</c> or <c>kits/sbox</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="ComponentKind.Workload"/> or <see cref="ComponentKind.Mixin"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The image's registry name and tag; the tag is derived from the input hash.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>The manifest digest, which a push keeps.</summary>
    public string Digest { get; set; } = string.Empty;

    /// <summary>The content ID the host image store reports, which changes when the image is replaced.</summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>The hash of everything the image is built from.</summary>
    public string InputHash { get; set; } = string.Empty;

    /// <summary>The text of the component's kit descriptor.</summary>
    public string Descriptor { get; set; } = string.Empty;

    /// <summary>The file name of the component's kit descriptor, such as <c>sbox.yaml</c>.</summary>
    public string DescriptorFile { get; set; } = string.Empty;

    /// <summary>The kit directory the image was built from.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>The image pinned by its digest, as a set or a thin kit references it.</summary>
    [JsonIgnore]
    public string Pinned => ImageReferences.PinnedTo(Reference, Digest);
}

/// <summary>A kit of a local build.</summary>
internal sealed class ManifestKit
{
    /// <summary>The name the configuration gave the kit.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>What sbx is given: a directory or a reference.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Whether the kit is built from a local directory.</summary>
    public bool IsLocal { get; set; }

    /// <summary>The digest a registry kit resolved to at build time; null for a local kit.</summary>
    public string? Digest { get; set; }

    /// <summary>The name of a local kit's component image; null for a registry kit.</summary>
    public string? Component { get; set; }

    /// <summary>A local kit's folder in the repository, which its input hash covers; null for a registry kit.</summary>
    public string? Source { get; set; }

    /// <summary>A registry kit pinned to the digest it resolved to at build time; as written when it has none.</summary>
    [JsonIgnore]
    public string PinnedReference =>
        Digest is { } digest ? ImageReferences.PinnedTo(Reference, digest) : Reference;

    /// <summary>The values for the kit's arguments.</summary>
    public Dictionary<string, string> Arguments { get; set; } = [];
}

/// <summary>An MCP server of a local build.</summary>
internal sealed class ManifestMcp
{
    /// <summary>The name of the server.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The endpoint of the server, when the squad declares one.</summary>
    public string? Url { get; set; }
}

/// <summary>A state directory of a local build.</summary>
internal sealed class ManifestState
{
    /// <summary>Where the directory is in the sandbox.</summary>
    public string SandboxPath { get; set; } = string.Empty;

    /// <summary>The path relative to the sandbox home.</summary>
    public string RelativePath { get; set; } = string.Empty;
}
