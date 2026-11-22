using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal sealed class UserSecretsLifecycleFixture : IDisposable
{
    private readonly UserSecretsCliHarness cli = new(existingSandbox: false);
    public TempDirectory Root => cli.Root;
    public TestConsole Console => cli.Console;
    public IPreflightChecks Preflight => cli.Preflight;
    public ISandboxLifecycle Lifecycle => cli.Lifecycle;
    public ISandboxInspection Inspection => cli.Inspection;
    public ISandboxExec Exec => cli.Exec;
    public ISandboxUserSecrets UserSecrets => cli.UserSecrets;
    public string Location => cli.Location;
    public string Name => cli.Name;
    public SandboxRecordStore Records { get; }
    public BuildStore Builds { get; }
    public static readonly DateTimeOffset OldBuild = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public UserSecretsLifecycleFixture()
    {
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        Root.File("teams/sample/bd.abox", "workload: sample\n");
        var paths = AboxPaths.For(Root.CreateDirectory("config"), Root.CreateDirectory("state"));
        Records = new SandboxRecordStore(paths);
        Builds = new BuildStore(paths);
        Builds.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                RootDirectory = Root.Path,
                Reference = ImageName.OfTeam("sample", "latest"),
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                LocalWorkload = LocalWorkloadFactory.Artifact(Root),
            }
        );
        Lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(""));
        Lifecycle
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, ""));
        var services = cli.Services;
        services.AddSingleton(
            new AboxConfig
            {
                DefaultTeam = "sample",
                DefaultLocation = Location,
                UserSecretsDenyList = ["prod.example"],
            }
        );
        services.AddSingleton(paths);
        services.AddSingleton<IProcessRunner>(LocalWorkloadFactory.Runner());
        services.AddSingleton(PublishedSquadFactory.Source());
    }

    public void Configure(string yaml) => cli.Configure(yaml);

    public void ConfigureForUser(string yaml) => cli.ConfigureForUser(yaml);

    public string Store(string id, string json) => cli.Store(id, json);

    public Task<int> Create(bool local = false, params string[] args) =>
        cli.Invoke(["create", .. local ? new[] { "--local", "--root", Root.Path } : [], .. args]);

    public void Existing()
    {
        Records.Save(
            Name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = OldBuild,
            }
        );
        cli.ExistingSandbox(OldBuild);
    }

    public Task<int> Replace(string entry) =>
        entry == "run" ? Run() : Create(entry == "local", "--squad", "ac", "--yes");

    public Task<int> Run() => cli.Invoke("run");

    public Task<int> OnDemand(params string[] args) => cli.Invoke(["user-secrets", .. args]);

    public Task<int> CreateShared(CancellationToken cancellationToken) =>
        CommandHandling.RunAsync(
            async () =>
                await cli
                    .Provider.GetRequiredService<SandboxCreator>()
                    .CreateAsync(
                        new SandboxCreationRequest(
                            new("sample", "", ["ac"]),
                            "ac",
                            Location,
                            "latest",
                            await FolderConfig.LoadAsync(Location, cancellationToken),
                            AssumeYes: true
                        ),
                        cancellationToken
                    ),
            Console
        );

    public Task<int> OrdinaryRun()
    {
        var record = Records.Load(Name)!;
        record.BuildCreatedAt = DateTimeOffset.MaxValue;
        Records.Save(Name, record);
        return Run();
    }

    public async Task NoReplacementOrCopy()
    {
        await Lifecycle
            .DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await Lifecycle
            .DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        await NoCopy();
    }

    public async Task NoCopy()
    {
        await UserSecrets
            .DidNotReceive()
            .GetUserSecretsHostPortsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await UserSecrets
            .DidNotReceive()
            .ValidateUserSecretsDestinationAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        await UserSecrets
            .DidNotReceive()
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    public void Dispose() => cli.Dispose();
}
