using Abox.Cli;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class CreateCommandTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private CliTestApplication? _application;
    private readonly TestConsole _console = new();
    private readonly ISandboxLifecycle _lifecycle = Substitute.For<ISandboxLifecycle>();
    private readonly IKitSourceTrust _kitSources = Substitute.For<IKitSourceTrust>();

    public void Dispose()
    {
        _application?.Dispose();
        _root.Dispose();
    }

    [Fact]
    public async Task RemoteCreateFromAnArbitraryDirectoryDoesNotNeedTeamsOrABuild()
    {
        // Arrange
        var command = Command("");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);
        Directory.Delete(Path.Combine(_root.Path, "config"), recursive: true);
        Directory.Delete(Path.Combine(_root.Path, "state"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(command, ["--team", "mti-ca", "--squad", "ac"]);

        // Assert
        _console.AllText.Should().NotContain("There is no team 'mti-ca'");
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource.Contains("agentic-box-rwest-mti-ca/ac")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemoteCreateWithoutSquadRequiresItRatherThanLookingForLocalTeams()
    {
        // Arrange
        var command = Command("");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(command, ["--team", "mti-ca"]);

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("requires --squad").And.NotContain("There is no team");
        await _lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateHasNoRootOptionBecauseItReadsNoTeamSources()
    {
        // Arrange
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(
            command,
            ["--local", "--squad", "ac", "--tag", "release", "--root", _root.Path]
        );

        // Assert
        exitCode.Should().NotBe(0);
        _lifecycle.ReceivedCalls().Should().BeEmpty();
        _kitSources.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ASquadThatConflictsWithTheFolderConfigurationIsRejectedBeforeAnySbxCalls()
    {
        // Arrange
        var command = Command("");
        _root.File("abox.yaml", "squad: other\n");

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(1);
        _console
            .Errors.Should()
            .Equal(
                "error: The selected squad conflicts with 'squad' in abox.yaml or abox.user.yaml. Use the folder's squad or update the folder configuration before creating or replacing its sandbox."
            );
        _lifecycle.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task LocalCreateFromAnArbitraryDirectoryDoesNotNeedTeams()
    {
        // Arrange
        var command = Command("");
        Directory.Delete(Path.Combine(_root.Path, "teams"), recursive: true);

        // Act
        var exitCode = await InvokeAsync(command, ["--local", "--squad", "ac", "--tag", "release"]);

        // Assert
        _console.AllText.Should().NotContain("There is no team");
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == Path.Combine(_root.Path, "runtime", "workload")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LocalCreateWithoutSquadRequiresItRatherThanLookingForLocalTeams()
    {
        // Arrange
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(command, ["--local", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(1);
        _console.AllText.Should().Contain("requires --squad").And.NotContain("Which squad");
        await _lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LocalCreationUsesTheLocalKitsInsteadOfThePullRepository()
    {
        // Arrange
        var command = Command("pullRepository: team-docker-release");

        // Act
        var exitCode = await InvokeAsync(command, ["--local", "--squad", "ac", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == Path.Combine(_root.Path, "runtime", "workload")
                    && request.PullPolicy == PullPolicy.Never
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LocalCreateAcceptsSquadNamesInAnotherCase()
    {
        // Arrange
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(command, ["--local", "--squad", "AC", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task PullingUsesTheDefaultTagFromSettings()
    {
        // Arrange
        var command = Command("defaultTag: release\npullRepository: team-docker-release\n");

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac"]);

        // Assert
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource
                    == $"rwest.jfrog.io/team-docker-release/agentic-box-rwest-sample/ac@{PublishedSquadFactory.Digest}"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheSandboxLocationCanBeOutsideTheTeamSourceProject()
    {
        // Arrange
        using var workspace = new TempDirectory();
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(
            command,
            ["--squad", "ac", "--tag", "release", "--location", workspace.Path]
        );

        // Assert
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request => request.Location == workspace.Path),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GivenMemoryAndCpusSizeTheSandboxAndAreSavedInTheUserFile()
    {
        // Arrange
        var command = Command("");
        _root.File("abox.user.yaml", "# my settings\nsquad: ac\nmemory: 4\n");

        // Act
        var exitCode = await InvokeAsync(
            command,
            ["--tag", "release", "--memory", "8", "--cpus", "4"]
        );

        // Assert
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request => request.Memory == 8 && request.Cpus == 4),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(Path.Combine(_root.Path, "abox.user.yaml"))
            .Should()
            .Be("# my settings\nsquad: ac\nmemory: 8\ncpus: 4\n");
    }

    [Fact]
    public async Task WithoutMemoryAndCpusTheFolderConfigurationSizesTheSandbox()
    {
        // Arrange
        var command = Command("");
        _root.File("abox.yaml", "memory: 16\ncpus: 2\n");
        _root.File("abox.user.yaml", "cpus: 8\n");

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request => request.Memory == 16 && request.Cpus == 8),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(Path.Combine(_root.Path, "abox.user.yaml")).Should().Be("cpus: 8\n");
    }

    [Fact]
    public async Task WithoutAnySizeSbxKeepsItsDefaultsAndNoUserFileIsWritten()
    {
        // Arrange
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac", "--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
        await _lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.Memory == null && request.Cpus == null
                ),
                Arg.Any<CancellationToken>()
            );
        File.Exists(Path.Combine(_root.Path, "abox.user.yaml")).Should().BeFalse();
    }

    [Theory]
    [InlineData("--memory")]
    [InlineData("--cpus")]
    public async Task ASizeBelowOneIsRejectedBeforeAnySbxCalls(string option)
    {
        // Arrange
        var command = Command("");

        // Act
        var exitCode = await InvokeAsync(command, ["--squad", "ac", option, "0"]);

        // Assert
        exitCode.Should().Be(1);
        _console.Errors.Should().Equal($"error: {option} must be a whole number of at least 1.");
        _lifecycle.ReceivedCalls().Should().BeEmpty();
        File.Exists(Path.Combine(_root.Path, "abox.user.yaml")).Should().BeFalse();
    }

    [Fact]
    public async Task EachStepIsAnnouncedBeforeItRuns()
    {
        // Arrange
        var command = Command("");
        _root.File("abox.yaml", "squad: ac\n");

        // Act
        var exitCode = await InvokeAsync(command, ["--tag", "release"]);

        // Assert
        exitCode.Should().Be(0);
        _console
            .Output.Where(line => line.EndsWith("...", StringComparison.Ordinal))
            .Select(line => line.Split(' ', 2)[0])
            .Should()
            .Equal(
                "Checking", // sbx
                "Finding", // the application ports
                "Reading", // the published squad set
                "Looking", // for an existing sandbox
                "Checking", // the credentials
                "Preparing", // the state directories
                "Creating", // the sandbox
                "Copying", // the user secrets
                "Publishing" // the application ports
            );
    }

    private async Task<int> InvokeAsync(CliTestApplication application, string[] arguments)
    {
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_root.Path);
            return await application
                .Provider.GetRequiredService<AboxRootCommand>()
                .InvokeAsync(["create", .. arguments]);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    private CliTestApplication Command(string yaml)
    {
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _root.File("config/config.yaml", yaml);
        var paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        var loaded = AboxConfigLoader.Load(paths, new YamlDotNetParser());
        var config = new AboxConfig
        {
            DefaultTeam = "sample",
            DefaultLocation = _root.Path,
            DefaultTag = loaded.DefaultTag,
            PullRepository = loaded.PullRepository,
        };
        var store = new BuildStore(paths);
        store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "release",
                Reference = "agentic-box-rwest-sample:release",
                WorkloadKitDirectory = "/kits/workload",
                LocalWorkload = LocalWorkloadFactory.Artifact(_root),
            }
        );
        ReadyRuntime();
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("docker").Returns(true);
        _application = new CliTestApplication(_console, paths, new TrailingArguments());
        _application
            .Services.AddSingleton(config)
            .AddSingleton(ReadyPreflight.Checks())
            .AddSingleton(_lifecycle)
            .AddSingleton(_kitSources)
            .AddSingleton(Substitute.For<IMcpServerRegistry>())
            .AddSingleton(PublishedSquadFactory.Source())
            .AddSingleton(SandboxFactory.Exec())
            .AddSingleton(LocalWorkloadFactory.Runner())
            .AddSingleton(programs)
            .AddSingleton(Substitute.For<IHostUserSecretStores>())
            .AddSingleton(Substitute.For<ISandboxUserSecrets>());
        return _application;
    }

    private void ReadyRuntime()
    {
        _lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(string.Empty));
    }
}
