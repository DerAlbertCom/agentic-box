using System.Collections.Concurrent;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

/// <summary>A synthetic Docker for local component images and registry digests.</summary>
internal static class LocalWorkloadFactory
{
    /// <summary>The content ID the synthetic image store reports for every image.</summary>
    public static readonly string ImageId = "sha256:" + new string('a', 64);

    // The kit directory each built reference came from, so a saved image carries its files.
    private static readonly ConcurrentDictionary<string, string> Built = new();

    public static IProcessRunner Runner()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Result(call.Arg<ProcessRequest>()));
        return runner;
    }

    /// <summary>Host programs where Docker is installed.</summary>
    public static IHostPrograms Programs()
    {
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("docker").Returns(true);
        return programs;
    }

    /// <summary>The digest the synthetic Docker reports for a reference: built, pushed or resolved.</summary>
    public static string Digest(string reference) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reference)));

    public static ProcessResult Result(ProcessRequest request)
    {
        var arguments = request.Arguments.ToList();
        if (arguments.Contains("--metadata-file"))
        {
            var reference = arguments[arguments.IndexOf("--tag") + 1];
            Built[reference] = arguments[^1];
            File.WriteAllText(
                arguments[arguments.IndexOf("--metadata-file") + 1],
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["containerimage.digest"] = Digest(reference) }
                )
            );
        }

        if (arguments.Contains("save") && arguments.Contains("image"))
        {
            SaveImage(arguments[arguments.IndexOf("--output") + 1], arguments[^1]);
        }

        return new(
            0,
            arguments.Contains("{{.Id}}") ? ImageId
                : arguments.Contains("{{json .Manifest}}")
                    ? JsonSerializer.Serialize(new { digest = Digest(arguments[^1]) })
                : arguments.Contains("ls") ? "{\"Name\":\"test-context\",\"Driver\":\"docker\"}"
                : arguments.SequenceEqual(new[] { "context", "show" }) ? "test-context"
                : "",
            ""
        );
    }

    /// <summary>The local image store of the synthetic Docker.</summary>
    public static DockerComponentImages Images(
        IHostPrograms? programs = null,
        IProcessRunner? runner = null
    ) => new(programs ?? Programs(), runner ?? Runner());

    /// <summary>The local squad source on the synthetic Docker, or on the given runner.</summary>
    public static LocalSquadSource Source(
        BuildStore store,
        IProcessRunner? runner = null,
        IHostPrograms? programs = null,
        IConsole? console = null
    )
    {
        var images = Images(programs, runner);
        return new(store, images, images, new YamlDotNetParser(), console ?? new TestConsole());
    }

    /// <summary>
    /// Gives a hand-made build record the component images a build would have made: one per
    /// local kit, state and configuration, in the synthetic image store, with a configuration
    /// image that bundles the record's metadata.
    /// </summary>
    /// <param name="workload">The directory the workload image was built from, if it matters.</param>
    public static BuildManifest WithImages(
        TempDirectory root,
        BuildManifest build,
        string? workload = null
    )
    {
        if (!ImageName.TryParseTeam(build.Reference, build.Tag, out var team))
            throw new InvalidOperationException("The build record has no team.");
        var repository =
            $"rwest.jfrog.io/{build.PublishRepository ?? "test-images-local"}/"
            + new SquadBuildIdentity(team, build.Squad, build.Tag).Repository
            + "/components/";
        // A tag of its own keeps parallel tests with the same squad apart.
        var tag = Guid.NewGuid().ToString("N");
        BuildComponent Component(string name, string kind, string descriptor)
        {
            var reference = $"{repository}{name}:{tag}";
            return new BuildComponent
            {
                Name = name,
                Kind = kind,
                Reference = reference,
                Digest = Digest(reference),
                ImageId = ImageId,
                InputHash = tag,
                Descriptor = descriptor,
                DescriptorFile = name.Split('/')[^1] + ".yaml",
            };
        }

        // A build always dates its configuration; squad.json without a date is invalid.
        if (build.CreatedAt == default)
            build.CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        build.ImageStore = "test-context";
        build.Components = [Component("workload", ComponentKind.Workload, "kind: workload\n")];
        // Like a build, every component image is built from a folder.
        var images = root.CreateDirectory(Path.Combine("images", tag));
        build.Components[0].Directory =
            workload ?? root.CreateDirectory(Path.Combine("images", tag, "workload"));
        foreach (var kit in build.Kits.Where(kit => kit.IsLocal))
        {
            kit.Component = ComponentNames.Kit(kit.Name);
            var component = Component(kit.Component, ComponentKind.Mixin, "kind: mixin\n");
            // Like a build, the input hash of a local kit covers its folder in the repository.
            if (Directory.Exists(kit.Reference))
            {
                component.Directory = kit.Reference;
                component.InputHash = BuildSources.InputHash(
                    ComponentKind.Mixin,
                    kit.Reference,
                    kit.Arguments
                );
            }
            build.Components.Add(component);
        }
        if (build.State.Count > 0)
        {
            build.Components.Add(
                Component(
                    "state",
                    ComponentKind.Mixin,
                    "kind: mixin\ncapabilities:\n"
                        + string.Concat(
                            build.State.Select(state =>
                                "  - type: com.docker.sandbox/host-mount@1\n    config:\n      path: "
                                + JsonSerializer.Serialize(state.SandboxPath)
                                + "\n"
                            )
                        )
                )
            );
            build.Components[^1].Directory = root.CreateDirectory(
                Path.Combine("images", tag, "state")
            );
        }
        var config = Component("config", ComponentKind.Mixin, "kind: mixin\n");
        build.Components.Add(config);
        config.Directory = SquadConfigurationMixin.Generate(
            team,
            build,
            [],
            Path.Combine(images, "config")
        );
        Load(config.Reference, config.Directory);
        return build;
    }

    /// <summary>The kit directory a synthetic image was built from, or null when it was not.</summary>
    public static string? BuiltFrom(string reference) =>
        Built.TryGetValue(reference, out var directory) ? directory : null;

    /// <summary>
    /// The runtime metadata the configuration image of a build bundles, which the build
    /// record itself does not keep.
    /// </summary>
    public static BuildManifest Bundled(BuildManifest build)
    {
        using var squad = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(build.ConfigurationMixinDirectory!, "squad.json"))
        );
        return new BuildManifest
        {
            Squad = build.Squad,
            Tag = build.Tag,
            CreatedAt = squad.RootElement.GetProperty("CreatedAt").GetDateTimeOffset(),
            Mcp = squad.RootElement.GetProperty("Mcp").Deserialize<List<ManifestMcp>>()!,
            State = squad.RootElement.GetProperty("State").Deserialize<List<ManifestState>>()!,
        };
    }

    /// <summary>Puts an image built from <paramref name="directory"/> into the synthetic image store.</summary>
    public static void Load(string reference, string directory) => Built[reference] = directory;

    /// <summary>Saves an image as Docker does: one layer with the files of the kit it was built from.</summary>
    private static void SaveImage(string archive, string reference)
    {
        using var layer = new MemoryStream();
        using (var writer = new TarWriter(layer, leaveOpen: true))
        {
            if (
                Built.TryGetValue(reference, out var directory)
                && File.Exists(Path.Combine(directory, "squad.json"))
            )
            {
                Write(
                    writer,
                    "usr/share/abox/squad.json",
                    File.ReadAllBytes(Path.Combine(directory, "squad.json"))
                );
            }
        }

        using var file = File.Create(archive);
        using var outer = new TarWriter(file);
        Write(outer, "manifest.json", Encoding.UTF8.GetBytes("[{\"Layers\":[\"layer.tar\"]}]"));
        Write(outer, "layer.tar", layer.ToArray());
    }

    private static void Write(TarWriter writer, string name, byte[] bytes)
    {
        using var data = new MemoryStream(bytes);
        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });
    }
}
