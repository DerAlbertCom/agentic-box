using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

internal sealed class SandboxCreatorFixture
{
    internal TempDirectory Root { get; } = new();
    internal TestConsole Console { get; } = new();
    internal ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    internal IHostSecretStore HostSecrets { get; } = TestServiceGraphs.HostSecretStore();
    internal ICredentialApprovals Approvals { get; } = Substitute.For<ICredentialApprovals>();
    internal ISandboxLifecycle Lifecycle { get; } = Substitute.For<ISandboxLifecycle>();
    internal IMcpServerRegistry Mcp { get; } = Substitute.For<IMcpServerRegistry>();
    internal IKitSourceTrust KitSources { get; } = Substitute.For<IKitSourceTrust>();
    internal IPublishedSquadSource Published { get; } = PublishedSquadFactory.Source();
    internal AboxPaths Paths { get; }
    internal BuildStore Store { get; }
    internal SandboxRecordStore Records { get; }
    internal SandboxCreator Service { get; }

    internal SandboxCreatorFixture()
    {
        Paths = AboxPaths.For(Path.Combine(Root.Path, "config"), Path.Combine(Root.Path, "state"));
        Store = new BuildStore(Paths);
        Records = new SandboxRecordStore(Paths);
        Service = Creator();
        Lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(string.Empty));
        Lifecycle
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, string.Empty));
        Mcp.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        Mcp.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new McpServerRegistration(true, string.Empty));
    }

    internal SandboxCreator Creator(
        RemoteSquadSource? remote = null,
        LocalWorkloadImages? images = null
    ) =>
        TestServiceGraphs.Creator(
            Records,
            Paths,
            Sbx,
            Lifecycle,
            Mcp,
            Console,
            new LocalSquadSource(Store, images ?? LocalWorkloadFactory.Images(), Console),
            remote ?? new RemoteSquadSource(new AboxConfig(), KitSources, Published),
            new UserSecretsCopy(
                new AboxConfig(),
                Substitute.For<IHostUserSecretStores>(),
                Substitute.For<ISandboxUserSecrets>()
            ),
            HostSecrets,
            Approvals
        );

    internal SandboxCreationRequest Request() =>
        new(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            "ac",
            Root.Path,
            "latest",
            FolderConfig: null,
            Local: true
        );

    internal void SaveBuild(List<ManifestMcp>? mcp = null, string? reference = null)
    {
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = reference ?? ImageName.OfTeam("sample", "latest"),
                WorkloadKitDirectory = "/kits/workload",
                LocalWorkload = LocalWorkloadFactory.Artifact(Root),
                StateMixinDirectory = "/kits/state",
                Kits =
                [
                    new ManifestKit
                    {
                        Name = "pi-config",
                        Reference = "/kits/pi-config",
                        IsLocal = true,
                        Arguments = new Dictionary<string, string> { ["mode"] = "team" },
                    },
                ],
                State =
                [
                    new ManifestState
                    {
                        SandboxPath = "/home/agent/.config/opencode/state",
                        RelativePath = ".config/opencode/state",
                    },
                ],
                Mcp = mcp ?? [],
            }
        );
    }
}
