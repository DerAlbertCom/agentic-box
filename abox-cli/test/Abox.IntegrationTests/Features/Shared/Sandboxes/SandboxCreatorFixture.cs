using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

internal sealed class SandboxCreatorFixture
{
    internal TempDirectory Root { get; } = new();
    internal TestConsole Console { get; }
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

    internal SandboxCreatorFixture(bool interactive = true)
    {
        Console = new TestConsole(interactive);
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
        IProcessRunner? runner = null
    ) =>
        TestServiceGraphs.Creator(
            Records,
            Paths,
            Lifecycle,
            Mcp,
            Console,
            LocalWorkloadFactory.Source(Store, runner, console: Console),
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

    /// <summary>
    /// Saves a local build of squad <c>ac</c>: its record and, in the synthetic image store,
    /// its component images, with a configuration image that bundles the record's metadata.
    /// </summary>
    internal BuildManifest SaveBuild(
        List<ManifestMcp>? mcp = null,
        string? reference = null,
        List<CredentialRequest>? credentials = null,
        List<SecretDeclaration>? secrets = null
    )
    {
        var build = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            CreatedAt = new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.Zero),
            Reference = reference ?? ImageName.OfTeam("sample", "latest"),
            ImageStore = "test-context",
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
            Credentials = credentials ?? [],
            Secrets = secrets ?? [],
        };
        LocalWorkloadFactory.WithImages(Root, build);
        Store.Save(build);
        return build;
    }
}
