namespace Abox.Sbx;

/// <summary>
/// The raw squad metadata of a sandbox: the kit descriptors the kit frontend staged and
/// the bundled squad metadata. Parsing is left to the callers.
/// </summary>
/// <param name="Kits">The staged descriptors, one per kit folder.</param>
/// <param name="SquadJson">The text of <c>/usr/share/abox/squad.json</c>; null when the sandbox has none.</param>
public sealed record SandboxSquadFiles(IReadOnlyList<StagedKitDescriptor> Kits, string? SquadJson);

/// <summary>A kit descriptor as the kit frontend staged it in the sandbox.</summary>
/// <param name="Kit">The kit folder name under <c>/usr/share/sandbox/kit</c>.</param>
/// <param name="Yaml">The text of its <c>kit.yaml</c>.</param>
public sealed record StagedKitDescriptor(string Kit, string Yaml);
