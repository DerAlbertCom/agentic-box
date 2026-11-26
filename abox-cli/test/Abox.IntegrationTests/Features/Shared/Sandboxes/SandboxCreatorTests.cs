using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorTests
{
    private static readonly SquadBuildIdentity Identity = new("sample", "ac", "latest");

    private SandboxCreator Creator(
        RemoteSquadSource? remote = null,
        IProcessRunner? runner = null
    ) => _fixture.Creator(remote, runner);

    private readonly SandboxCreatorFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;
    private TestConsole Console => _fixture.Console;
    private ISandboxLifecycle Lifecycle => _fixture.Lifecycle;
    private IMcpServerRegistry Mcp => _fixture.Mcp;
    private IKitSourceTrust KitSources => _fixture.KitSources;
    private IPublishedSquadSource Published => _fixture.Published;
    private AboxPaths Paths => _fixture.Paths;
    private BuildStore Store => _fixture.Store;
    private SandboxRecordStore Records => _fixture.Records;
    private SandboxCreator Service => _fixture.Service;

    [Fact]
    public async Task ASandboxIsCreatedFromTheLocalBuildThroughThinKitsOverItsImages()
    {
        // Arrange
        SaveBuild();
        var kits = Path.Combine(Store.DirectoryFor(Identity), LocalSquadSource.ThinKitsFolderName);

        // Act
        var exitCode = await Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        Published.ReceivedCalls().Should().BeEmpty("--local must not pull registry metadata");
        await KitSources
            .DidNotReceive()
            .EnsureTrustedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource == Path.Combine(kits, "workload")
                    && request.HostImageStore == "test-context"
                    && request.PullPolicy == PullPolicy.Never
                    && request.Name == SandboxName.For(Root.Path)
                    && request.Mixins.SequenceEqual(
                        new[]
                        {
                            Path.Combine(kits, "kits-pi-config"),
                            Path.Combine(kits, "state"),
                            Path.Combine(kits, "config"),
                        }
                    )
                    && request.KitArguments["mode"] == "team"
                ),
                Arg.Any<CancellationToken>()
            );
        var images = Store
            .Load(Identity)!
            .Components.ToDictionary(
                component => component.Name,
                component =>
                    component.Reference[..component.Reference.LastIndexOf(':')]
                    + "@"
                    + component.Digest
            );
        File.ReadAllText(Path.Combine(kits, "workload", "workload.dockerfile"))
            .Should()
            .Be($"FROM {images["workload"]}\n");
        File.ReadAllText(Path.Combine(kits, "workload", "workload.yaml"))
            .Should()
            .Be("kind: workload\n");
        File.ReadAllText(Path.Combine(kits, "kits-pi-config", "pi-config.dockerfile"))
            .Should()
            .Be($"FROM scratch\nCOPY --from={images["kits/pi-config"]} / /\n");
    }

    [Fact]
    public async Task TheRuntimeMetadataOfALocalSandboxComesFromItsConfigurationImage()
    {
        // Arrange
        SaveBuild([new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var record = Store.Load(Identity)!;
        record.Mcp.Clear();
        record.State.Clear();
        record.CreatedAt = DateTimeOffset.UnixEpoch;
        Store.Save(record);

        // Act
        var exitCode = await Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0, Console.AllText);
        await Mcp.Received(1)
            .AddAsync("elastic", "https://example.com/mcp", Arg.Any<CancellationToken>());
        var sandbox = Records.Load(SandboxName.For(Root.Path))!;
        sandbox
            .BuildCreatedAt.Should()
            .Be(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero));
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request => request.Mounts.Count == 1),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sha256:eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee")]
    public async Task AMissingOrReplacedComponentImageFailsBeforeTheExistingSandboxIsTouched(
        string? imageId
    )
    {
        // Arrange
        SaveBuild();
        var runner = LocalWorkloadFactory.Runner();
        runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("{{.Id}}")
                    && request.Arguments.Last().Contains("/components/state:")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                imageId is null
                    ? new ProcessResult(1, "", "No such image")
                    : new ProcessResult(0, imageId, "")
            );

        // Act
        var action = () =>
            Creator(runner: runner)
                .CreateAsync(Request() with { AssumeYes = true }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "The component image '*/components/state:*' is missing or was replaced. Run 'abox build --team sample --tag latest' again*"
        );
        await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LocalSquadNamesUseTheCanonicalCaseForBuildsAndRecords()
    {
        // Arrange
        SaveBuild();

        // Act
        var exitCode = await Service.CreateAsync(
            Request() with
            {
                Squad = "AC",
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        Records.Load(SandboxName.For(Root.Path))!.Squad.Should().Be("ac");
    }

    [Theory]
    [InlineData("team")]
    [InlineData("different")]
    public async Task SharedKitArgumentsMustHaveTheSameValue(string value)
    {
        // Arrange
        SaveBuild();
        var manifest = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        manifest.Kits.Add(
            new ManifestKit
            {
                Name = "other-kit",
                Reference = "/kits/other",
                Arguments = new Dictionary<string, string> { ["mode"] = value },
            }
        );
        Store.Save(manifest);

        if (value == "team")
        {
            // Act
            var exitCode = await Service.CreateAsync(Request(), CancellationToken.None);

            // Assert
            exitCode.Should().Be(0);
            await Lifecycle
                .Received(1)
                .CreateAsync(
                    Arg.Is<SandboxDefinition>(request =>
                        request.KitArguments.Count == 1 && request.KitArguments["mode"] == "team"
                    ),
                    Arg.Any<CancellationToken>()
                );
        }
        else
        {
            // Act
            var action = () => Service.CreateAsync(Request(), CancellationToken.None);

            // Assert
            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*mode*pi-config*other-kit*"
            );
            await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
            await Lifecycle
                .DidNotReceive()
                .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("mtiae-docker-release")]
    [InlineData("team-docker-release")]
    public async Task PublishedSetsArePulledFromTheConfiguredRepository(string repository)
    {
        // Arrange
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var service = Creator(
            new RemoteSquadSource(
                new AboxConfig { PullRepository = repository },
                KitSources,
                PublishedSquadFactory.Source([
                    new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" },
                ])
            )
        );

        // Act
        var exitCode = await service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await KitSources
            .Received(1)
            .EnsureTrustedAsync($"rwest.jfrog.io/{repository}/", Arg.Any<CancellationToken>());
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/{repository}/agentic-box-rwest-sample/ac@{PublishedSquadFactory.Digest}"
                    && request.PullPolicy == PullPolicy.Always
                    && request.HostImageStore == null
                    && request.Mixins.Count == 0
                    && request.KitArguments.Count == 0
                    && request.Mounts.Count == 1
                    && request.StaticMcpServers.SequenceEqual(new[] { "elastic" })
                ),
                Arg.Any<CancellationToken>()
            );
        Records
            .Load(SandboxName.For(Root.Path))!
            .KitReference.Should()
            .Be(
                $"rwest.jfrog.io/{repository}/agentic-box-rwest-sample/ac@{PublishedSquadFactory.Digest}"
            );
    }

    [Fact]
    public async Task PullFailuresAreReportedWithoutFallingBackToLocalKits()
    {
        // Arrange
        SaveBuild();
        Lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation("registry access denied", "registry access denied"));

        // Act
        var action = () =>
            Service.CreateAsync(Request() with { Local = false }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*registry access denied*");
        await Lifecycle
            .Received(1)
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        Records.Load(SandboxName.For(Root.Path)).Should().BeNull();
    }

    [Theory]
    [InlineData("bad/path", "sample", "ac", "latest", "pullRepository")]
    [InlineData("release", "bad/team", "ac", "latest", "team/squad")]
    [InlineData("release", "sample", "bad/squad", "latest", "team/squad")]
    [InlineData("release", "sample", "ac", "bad/tag", "registry tag")]
    public async Task InvalidPullReferencesFailBeforeAnySandboxIsTouched(
        string repository,
        string team,
        string squad,
        string tag,
        string error
    )
    {
        // Arrange
        var service = Creator(
            new RemoteSquadSource(
                new AboxConfig { PullRepository = repository },
                KitSources,
                Published
            )
        );
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam(team, "teams/sample", [squad]),
            squad,
            Root.Path,
            tag,
            FolderConfig: null
        );

        // Act
        var action = () => service.CreateAsync(request, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage($"*{error}*");
        await KitSources
            .DidNotReceive()
            .EnsureTrustedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Published.ReceivedCalls().Should().BeEmpty();
        await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("other-team")]
    [InlineData("")]
    public async Task PullingIgnoresLocalMetadataEvenWhenItBelongsToAnotherTeam(string team)
    {
        // Arrange
        Root.File(
            "state/builds/ac/latest/build.json",
            System.Text.Json.JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = team == "" ? "" : ImageName.OfTeam(team, "latest"),
                }
            )
        );

        // Act
        var exitCode = await Service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await Published
            .Received(1)
            .LoadAsync(Arg.Any<string>(), Identity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LocalCreateWithoutASquadAsksForNoneAndUsesNoTeamSources()
    {
        // Arrange
        SaveBuild();
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam("sample", string.Empty, []),
            null,
            Root.Path,
            "latest",
            FolderConfig: null,
            Local: true
        );

        // Act
        var action = () => Service.CreateAsync(request, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Local create requires --squad*"
        );
        Console.AllText.Should().NotContain("Which squad");
    }

    [Fact]
    public async Task ABuildThatIsNotThereIsAnError()
    {
        // Arrange: no local build has been saved in the fixture's store.

        // Act
        var action = () => Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("abox build --team sample");
    }

    [Fact]
    public async Task AnExistingSandboxIsReplacedAfterConfirmation()
    {
        // Arrange
        SaveBuild();
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });

        // Act
        var exitCode = await Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        await Lifecycle.Received(1).RemoveAsync(name, Arg.Any<CancellationToken>());
        Console.AllText.Should().Contain("already exists");
    }

    [Fact]
    public async Task AnExistingSandboxIsKeptWhenTheAnswerIsNo()
    {
        // Arrange
        SaveBuild();
        Console.Answer = false;
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });

        // Act
        var action = () => Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("not replaced");
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecliningReplacementDoesNotRegisterMcpOrCreateStateDirectories(bool local)
    {
        // Arrange
        var mcp = new List<ManifestMcp>
        {
            new() { Name = "elastic", Url = "https://example.com/mcp" },
        };
        SaveBuild(mcp);
        var service = Creator(
            new RemoteSquadSource(new AboxConfig(), KitSources, PublishedSquadFactory.Source(mcp))
        );
        Console.Answer = false;
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.UtcNow) });

        // Act
        var action = () =>
            service.CreateAsync(Request() with { Local = local }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*not replaced*");
        await Mcp.DidNotReceive()
            .AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(Paths.StateDirectory, name)).Should().BeFalse();
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task McpRegistrationFailureAfterConsentDoesNotRemoveTheExistingSandbox()
    {
        // Arrange
        SaveBuild([new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.UtcNow) });
        Mcp.AddAsync("elastic", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Console.AllText.Should().Contain("Replace the sandbox?");
                return new McpServerRegistration(false, "registration failed");
            });

        // Act
        var action = () => Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*could not be registered*"
        );
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(Paths.StateDirectory, name)).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", null, "*missing*not registered*")]
    [InlineData("elastic,other", null, "*mcp.name*not valid*")]
    [InlineData("other", "file:///local", "*mcp.url*not valid*")]
    [InlineData("other", "https://user:password@example.com/mcp", "*mcp.url*not valid*")]
    public async Task AllMcpRegistrationsAreValidatedReadOnlyBeforeAskingForReplacement(
        string name,
        string? url,
        string error
    )
    {
        // Arrange
        SaveBuild([
            new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" },
            new ManifestMcp { Name = name, Url = url },
        ]);

        // Act
        var action = () => Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .WithMessage(error)
            .Which.Message.Should()
            .NotContain("password");
        await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await Mcp.DidNotReceive()
            .AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory
            .Exists(Path.Combine(Paths.StateDirectory, SandboxName.For(Root.Path)))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task TheStateDirectoriesAreKeptOnTheHostUnderTheNameOfTheSandbox()
    {
        // Arrange
        SaveBuild();

        // Act
        await Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var name = SandboxName.For(Root.Path);
        var hostPath = Path.Combine(Paths.StateDirectory, name, ".config", "opencode", "state");
        Directory.Exists(hostPath).Should().BeTrue();

        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.Mounts.Count == 1
                    && Path.GetFullPath(request.Mounts[0].HostPath) == Path.GetFullPath(hostPath)
                    && request.Mounts[0].SandboxPath == "/home/agent/.config/opencode/state"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheMcpServersAreRegisteredWhenTheyHaveAnUrl()
    {
        // Arrange
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);

        // Act
        await Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        await Mcp.Received(1)
            .AddAsync("elastic", "https://example.com/mcp", Arg.Any<CancellationToken>());
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.StaticMcpServers.SequenceEqual(new[] { "elastic" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnMcpServerWithoutAnUrlHasToBeRegisteredAlready()
    {
        // Arrange
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic" }]);

        // Act
        var action = () => Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task TheRecordOfTheSandboxIsKept()
    {
        // Arrange
        SaveBuild();

        // Act
        await Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var record = Records.Load(SandboxName.For(Root.Path));
        record.Should().NotBeNull();
        record!.Squad.Should().Be("ac");
        record.Tag.Should().Be("latest");
    }

    private SandboxCreationRequest Request() => _fixture.Request();

    [Fact]
    public async Task OldLocalManifestsAreRejectedWithoutTouchingAnExistingSandbox()
    {
        // Arrange
        SaveBuild();
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Components.Clear();
        Store.Save(build);

        // Act
        var action = () => Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*has no component images (an older build)*abox build --team sample --tag latest*"
        );
        await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoteCreationConsumesOnlyThePublishedSetEvenWithoutALocalImage()
    {
        // Arrange
        SaveBuild();
        var build = Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Components.Clear();
        Store.Save(build);
        var runner = LocalWorkloadFactory.Runner();
        var service = Creator(runner: runner);

        // Act
        var exitCode = await service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/mtiae-docker-release/agentic-box-rwest-sample/ac@{PublishedSquadFactory.Digest}"
                    && request.Mixins.Count == 0
                    && request.KitArguments.Count == 0
                    && request.HostImageStore == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemoteCreateNeedsNoLocalBuildAndUsesPublishedState()
    {
        // Arrange: the fixture has published metadata but no local build.

        // Act
        var exitCode = await Service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await Lifecycle
            .Received(1)
            .CreateAsync(
                Arg.Is<SandboxDefinition>(request =>
                    request.Mounts[0].SandboxPath == "/home/agent/.published"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UnreadablePublishedMetadataCannotDeleteAnExistingSandboxEvenWithYes()
    {
        // Arrange
        SaveBuild();
        Published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<PublishedSquad>(_ =>
                throw new AboxException("Invalid bundled metadata; republish the set.")
            );
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.UtcNow) });

        // Act
        var action = () =>
            Service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*republish*");
        // Only repository trust may be updated before published metadata is validated.
        KitSources.ReceivedCalls().Should().ContainSingle();
        Mcp.ReceivedCalls().Should().BeEmpty();
        Lifecycle.ReceivedCalls().Should().BeEmpty();
        Records.Load(name).Should().BeNull();
    }

    [Fact]
    public async Task RemoteRepositoryTrustIsEnsuredBeforeRegistryWorkAndSandboxReplacement()
    {
        // Arrange
        var calls = new List<string>();
        KitSources
            .When(trust =>
                trust.EnsureTrustedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("trust"));
        Published
            .When(source =>
                source.LoadAsync(
                    Arg.Any<string>(),
                    Arg.Any<SquadBuildIdentity>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => calls.Add("registry"));
        Lifecycle
            .When(lifecycle =>
                lifecycle.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("remove"));
        Lifecycle
            .When(lifecycle =>
                lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("create"));
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                new[]
                {
                    new SandboxInfo(SandboxName.For(Root.Path), "running", DateTimeOffset.UtcNow),
                }
            );

        // Act
        await Service.CreateAsync(
            Request() with
            {
                Local = false,
                AssumeYes = true,
            },
            CancellationToken.None
        );

        // Assert
        calls.Should().Equal("trust", "registry", "remove", "create");
    }

    [Fact]
    public async Task DeniedRepositoryTrustCannotPullOrRemoveAnExistingSandboxEvenWithYes()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.UtcNow) });
        KitSources
            .EnsureTrustedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                throw new AboxException("Administrator policy denies kit.allowedSources update.")
            );

        // Act
        var action = () =>
            Service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Administrator policy*kit.allowedSources*"
        );
        Published.ReceivedCalls().Should().BeEmpty();
        await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        Records.Load(name).Should().BeNull();
        Directory.Exists(Path.Combine(Paths.StateDirectory, name)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingPublishedMcpRegistrationFailsBeforeSandboxReplacement()
    {
        // Arrange
        Published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new PublishedSquad(
                    "pinned-set",
                    new BuildManifest { Mcp = [new ManifestMcp { Name = "missing" }] }
                )
            );

        // Act
        var action = () =>
            Service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*not registered*");
        await Lifecycle.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnusablePublishedStateDirectoryFailsBeforeSandboxReplacement()
    {
        // Arrange
        var name = SandboxName.For(Root.Path);
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.UtcNow) });
        Root.File($"state/{name}/.published", "a file blocks the state directory");

        // Act
        var action = () =>
            Service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*state director*not replaced*"
        );
        await Lifecycle.Received(1).ListAsync(Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private void SaveBuild(List<ManifestMcp>? mcp = null, string? reference = null) =>
        _fixture.SaveBuild(mcp, reference);
}
