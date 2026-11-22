using Abox.Cli;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataOnlyPreparesRemoteMetadataWithoutAnyExternalPrograms(bool noVerify)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(
            "teams/sample/ac.abox",
            "workload: sample\nstate: [~/.probe-state]\nmcp: [elastic]\n"
        );
        root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        var console = new TestConsole();
        var preflight = Substitute.For<IPreflightChecks>();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var config = new AboxConfig();
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var store = new BuildStore(paths);
        var resolver = new SquadResolver(config, new YamlDotNetParser());
        var service = TestServiceGraphs.Build(
            resolver,
            new PersonalConfigurations(paths),
            store,
            LocalWorkloadFactory.Images(programs, runner, console),
            console
        );
        using var application = new CliTestApplication(console, paths, new TrailingArguments());
        application.Services.AddSingleton(config);
        application.Services.AddSingleton(preflight);
        application.Services.AddSingleton(resolver);
        application.Services.AddSingleton(service);
        var arguments = new List<string>
        {
            "build",
            "--team",
            "sample",
            "--root",
            root.Path,
            "--tag",
            "remote",
            "--metadata-only",
            "--no-personal",
        };
        if (noVerify)
        {
            arguments.Add("--no-verify");
        }

        // Act
        var exitCode = await application
            .Provider.GetRequiredService<AboxRootCommand>()
            .InvokeAsync(arguments.ToArray());

        // Assert
        exitCode.Should().Be(0);
        var manifest = store.Load(new SquadBuildIdentity("sample", "ac", "remote"))!;
        manifest.LocalWorkload.Should().BeNull();
        manifest.Reference.Should().Be(ImageName.OfTeam("sample", "remote"));
        manifest.State.Should().ContainSingle();
        manifest.Mcp.Should().ContainSingle().Which.Name.Should().Be("elastic");
        manifest.SourceHashes.Should().NotBeEmpty();
        File.Exists(Path.Combine(manifest.WorkloadKitDirectory, "workload.yaml")).Should().BeTrue();
        console.AllText.Should().Contain("metadata only").And.NotContain("--tag remote --local");
        preflight.ReceivedCalls().Should().BeEmpty();
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
    }
}
