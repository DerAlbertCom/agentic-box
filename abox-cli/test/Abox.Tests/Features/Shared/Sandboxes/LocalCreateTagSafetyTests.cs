using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class LocalCreateTagSafetyTests
{
    [Theory]
    [InlineData("resolve")]
    [InlineData("load")]
    [InlineData("create")]
    public async Task DirectLocalCreationRejectsUnsafeTagsBeforeReadingBuildsOrPrompting(
        string operation
    )
    {
        // Arrange
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var sentinel = outside.File("build.json", "external manifest must not be read");
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var tag = Path.GetRelativePath(Path.Combine(paths.BuildsDirectory, "ac"), outside.Path);
        var config = new AboxConfig();
        var console = new TestConsole();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var lifecycle = Substitute.For<ISandboxLifecycle>();
        var mcp = Substitute.For<IMcpServerRegistry>();
        var kitSources = Substitute.For<IKitSourceTrust>();
        var published = Substitute.For<IPublishedSquadSource>();
        var source = new LocalSquadSource(
            new BuildStore(paths),
            LocalWorkloadFactory.Images(programs, runner, console),
            console
        );
        var creator = TestServiceGraphs.Creator(
            new SandboxRecordStore(paths),
            paths,
            sbx,
            lifecycle,
            mcp,
            console,
            source,
            new RemoteSquadSource(config, kitSources, published),
            new UserSecretsCopy(
                config,
                Substitute.For<IHostUserSecretStores>(),
                Substitute.For<ISandboxUserSecrets>()
            )
        );
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam("sample", root.Path, ["ac", "bd"]),
            null,
            Path.Combine(root.Path, "missing-location"),
            tag,
            null,
            AssumeYes: true,
            Local: true
        );
        var create = Operation(operation, source, creator, request);

        // Act
        var result = await create.Should().ThrowAsync<AboxException>();

        // Assert
        result.WithMessage("*not a valid registry tag*");
        console
            .Output.Should()
            .BeEmpty("an invalid tag must not prompt for a squad or replacement");
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
        lifecycle.ReceivedCalls().Should().BeEmpty();
        mcp.ReceivedCalls().Should().BeEmpty();
        kitSources.ReceivedCalls().Should().BeEmpty();
        published.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(sentinel).Should().Be("external manifest must not be read");
        Directory.GetFiles(outside.Path, "*", SearchOption.AllDirectories).Should().Equal(sentinel);
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }

    private static Func<Task> Operation(
        string operation,
        LocalSquadSource source,
        SandboxCreator creator,
        SandboxCreationRequest request
    ) =>
        operation switch
        {
            "resolve" => () =>
            {
                source.Resolve(request);
                return Task.CompletedTask;
            },
            "load" => async () =>
            {
                await source.LoadAsync(
                    new SquadSelection(new SquadBuildIdentity("sample", "ac", request.Tag)),
                    CancellationToken.None
                );
            },
            _ => async () =>
            {
                await creator.CreateAsync(request, CancellationToken.None);
            },
        };
}
