using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal sealed class AppPortsLifecycleFixture : IDisposable
{
    private readonly UserSecretsCliHarness cli = new(existingSandbox: false);
    public TempDirectory Root => cli.Root;
    public TestConsole Console => cli.Console;
    public IPreflightChecks Preflight => cli.Preflight;
    public ISbxClient Sbx => cli.Sbx;
    public ISandboxLifecycle Lifecycle => cli.Lifecycle;
    public IKitSourceTrust KitSources => cli.KitSources;
    public ISandboxInspection Inspection => cli.Inspection;
    public IApplicationPortPublisher Publisher => cli.Publisher;
    public ISandboxExec Exec => cli.Exec;
    public ISandboxUserSecrets UserSecrets => cli.UserSecrets;
    public string Location => cli.Location;
    public string Name { get; set; }
    public SandboxRecordStore Records { get; }
    public IServiceCollection Services => cli.Services;
    public static readonly DateTimeOffset OldBuild = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public AppPortsLifecycleFixture()
    {
        Name = cli.Name;
        Root.File("teams/sample/ac.abox", "workload: sample\n");
        var paths = AboxPaths.For(Root.CreateDirectory("config"), Root.CreateDirectory("state"));
        Records = new SandboxRecordStore(paths);
        new BuildStore(paths).Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                RootDirectory = Root.Path,
                Reference = ImageName.OfTeam("sample", "latest"),
                CreatedAt = OldBuild.AddDays(1),
                LocalWorkload = LocalWorkloadFactory.Artifact(Root),
            }
        );
        Lifecycle
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxCreation(""));
        Lifecycle
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxRemoval(true, ""));
        Services.AddSingleton(
            new AboxConfig { DefaultTeam = "sample", DefaultLocation = Location }
        );
        Services.AddSingleton(paths);
        Services.AddSingleton<IProcessRunner>(LocalWorkloadFactory.Runner());
        Services.AddSingleton(PublishedSquadFactory.Source());
    }

    public void Angular() =>
        Root.File(
            "folder/angular.json",
            """{"projects":{"ui":{"architect":{"serve":{"options":{"port":4200}}}}}}"""
        );

    public void Web()
    {
        Root.File("folder/api/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
        Root.File(
            "folder/api/Properties/launchSettings.json",
            """{"profiles":{"https":{"applicationUrl":"https://localhost:8203"}}}"""
        );
        Sbx.GetDevelopmentCertificatePasswordAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new string('a', 64));
    }

    public void Configure(string json) => cli.Configure(json);

    public void HttpsOutcome(string stage, Task outcome)
    {
        if (stage == "activate")
            Sbx.ActivateDevelopmentCertificateAsync(
                    Arg.Any<string>(),
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(outcome);
        else
            Sbx.VerifyDevelopmentCertificateActivationAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(outcome);
    }

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
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(Name, "running", OldBuild) });
    }

    public Task<int> Replace(string entry) =>
        entry == "run" ? Invoke("run") : Create(entry == "local", "--yes");

    public Task<int> CreateShared(CancellationToken token) =>
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
                            await FolderConfig.LoadAsync(Location, token),
                            AssumeYes: true
                        ),
                        token
                    ),
            Console
        );

    public Task<int> Invoke(params string[] args) => cli.Invoke(args);

    public Task<int> Create(bool local, params string[] args) =>
        Invoke([
            "create",
            "--squad",
            "ac",
            .. local ? new[] { "--local", "--root", Root.Path } : [],
            .. args,
        ]);

    public void Dispose() => cli.Dispose();
}
