using Abox.Cli;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure.Dotnet;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Disposable synthetic host stores and the real CLI with fake external tools.</summary>
internal sealed class UserSecretsCliHarness : IDisposable
{
    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; }
    public IPreflightChecks Preflight { get; } = ReadyPreflight.Checks();
    public ISandboxExec Exec { get; } = SandboxFactory.Exec();
    public ISandboxUserSecrets UserSecrets { get; } = Substitute.For<ISandboxUserSecrets>();
    public string Location { get; }
    public string Name => SandboxName.For(Location);
    private readonly CliTestApplication _application;
    public ISandboxLifecycle Lifecycle => _application.Lifecycle;
    public IMcpServerRegistry Mcp => _application.Mcp;
    public IKitSourceTrust KitSources => _application.KitSources;
    public ISandboxInspection Inspection => _application.Inspection;
    public IServiceCollection Services => _application.Services;
    public IApplicationPortPublisher Publisher => _application.Publisher;
    public ISandboxDevelopmentCertificates Certificates => _application.Certificates;
    public IHostSecretStore HostSecrets => _application.HostSecrets;
    public IHostCommands HostCommands => _application.HostCommands;
    public ICredentialApprovals Approvals => _application.Approvals;
    public ISandboxSquadFileReader SquadFiles => _application.SquadFiles;
    public IServiceProvider Provider => _application.Provider;

    public UserSecretsCliHarness(
        AboxConfig? config = null,
        bool unix = false,
        string? configYaml = null,
        bool existingSandbox = true,
        bool interactive = true
    )
    {
        Console = new TestConsole(interactive);
        Location = Root.CreateDirectory("folder");
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        _application = new CliTestApplication(
            Console,
            AboxPaths.For(Root.Path, Root.Path),
            new TrailingArguments()
        ).WithSandboxSubstitutes();
        if (existingSandbox)
            ExistingSandbox();
        if (configYaml is null)
            Services.AddSingleton(config ?? new AboxConfig { DefaultLocation = Location });
        else
            Root.File("config.yaml", configYaml);
        Services.AddSingleton(
            (IHostUserSecretStores)
                new DotnetUserSecretStores(
                    unix ? null : Root.CreateDirectory("appdata"),
                    Root.CreateDirectory("home")
                )
        );
        Services.AddSingleton(Exec);
        Services.AddSingleton(UserSecrets);
        Services.AddSingleton(programs);
        Services.AddSingleton(Preflight);
    }

    public void ExistingSandbox(DateTimeOffset? createdAt = null) =>
        Lifecycle
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(Name, "running", createdAt) });

    public void Configure(string yaml) => Root.File("folder/abox.yaml", yaml);

    public void ConfigureForUser(string yaml) => Root.File("folder/abox.user.yaml", yaml);

    public string Store(string id, string json) =>
        Root.File($"appdata/Microsoft/UserSecrets/{id}/secrets.json", json);

    public Task<int> Invoke(params string[] args) =>
        Provider.GetRequiredService<AboxRootCommand>().InvokeAsync(args);

    public Task<int> Run(params string[] args) =>
        Invoke(["user-secrets", "--location", Location, .. args]);

    public Task<int> RunDefault() => Invoke("user-secrets");

    public async Task NoWrites() =>
        await UserSecrets
            .DidNotReceive()
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );

    public string Written(string id) => WrittenStores().Single(write => write.Id == id).Json;

    public string LatestWritten(string? id = null) =>
        WrittenStores().Last(write => id is null || write.Id == id).Json;

    private IEnumerable<(string Id, string Json)> WrittenStores() =>
        UserSecrets
            .ReceivedCalls()
            .Where(call =>
                call.GetMethodInfo().Name == nameof(ISandboxUserSecrets.WriteUserSecretsAsync)
            )
            .Select(call => ((string)call.GetArguments()[1]!, (string)call.GetArguments()[2]!));

    public void Dispose()
    {
        _application.Dispose();
        Root.Dispose();
    }
}
