using System.Text.Json;
using Abox.Cli;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class CreateTagCommandTests
{
    [Theory]
    [MemberData(nameof(InvalidTags))]
    public async Task UnsafeTagsFailBeforeFolderReadsPreflightOrRuntimeChanges(
        bool local,
        bool configuredTag,
        string input
    )
    {
        // Arrange
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var sentinel = outside.File("build.json", "external build");
        root.File("teams/sample/ac.abox", "workload: sample\n");
        root.File("abox.yaml", "not JSON; invalid tag must be reported first");
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var tag = UnsafeTag(input, paths, outside.Path);
        root.File(
            "config/config.yaml",
            "defaultTeam: sample\n"
                + (configuredTag ? "defaultTag: " + JsonSerializer.Serialize(tag) + "\n" : "")
        );
        var console = new TestConsole();
        var preflight = Substitute.For<IPreflightChecks>();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var lifecycle = Substitute.For<ISandboxLifecycle>();
        var kitSources = Substitute.For<IKitSourceTrust>();
        var published = Substitute.For<IPublishedSquadSource>();
        using var application = new CliTestApplication(console, paths, new TrailingArguments());
        application
            .Services.AddSingleton(preflight)
            .AddSingleton(programs)
            .AddSingleton(runner)
            .AddSingleton(lifecycle)
            .AddSingleton(kitSources)
            .AddSingleton(published)
            .AddSingleton(Substitute.For<IMcpServerRegistry>())
            .AddSingleton(Substitute.For<IHostUserSecretStores>())
            .AddSingleton(Substitute.For<ISandboxUserSecrets>());
        var arguments = new List<string>
        {
            "create",
            "--location",
            root.Path,
            "--squad",
            "ac",
            "--yes",
        };
        if (local)
            arguments.AddRange(["--local", "--root", root.Path]);
        if (!configuredTag)
            arguments.AddRange(["--tag", tag]);

        // Act
        var exitCode = await application
            .Provider.GetRequiredService<AboxRootCommand>()
            .InvokeAsync(arguments.ToArray());

        // Assert
        exitCode.Should().Be(1);
        console.AllText.Should().Contain("not a valid registry tag").And.Contain("1-128");
        preflight.ReceivedCalls().Should().BeEmpty();
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        lifecycle.ReceivedCalls().Should().BeEmpty();
        kitSources.ReceivedCalls().Should().BeEmpty();
        published.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(sentinel).Should().Be("external build");
        Directory.GetFiles(outside.Path, "*", SearchOption.AllDirectories).Should().Equal(sentinel);
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }

    private static string UnsafeTag(string input, AboxPaths paths, string outside) =>
        input switch
        {
            "traversal" => Path.GetRelativePath(Path.Combine(paths.BuildsDirectory, "ac"), outside),
            "absolute" => outside,
            "overlong" => new string('a', 129),
            _ => input,
        };

    public static IEnumerable<object[]> InvalidTags()
    {
        foreach (var tag in new[] { "traversal", "absolute", "", " ", "overlong", "a/b", "a\\b" })
        foreach (var local in new[] { false, true })
        foreach (var configuredTag in new[] { false, true })
        {
            yield return [local, configuredTag, tag];
        }
    }
}
