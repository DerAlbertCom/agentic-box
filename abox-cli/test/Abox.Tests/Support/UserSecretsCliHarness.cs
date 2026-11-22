using Abox.Cli;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure;
using Abox.Infrastructure.Dotnet;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Disposable synthetic host stores and the real CLI with fake external tools.</summary>
internal sealed class UserSecretsCliHarness : IDisposable
{
    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; } = new();
    public IPreflightChecks Preflight { get; } = ReadyPreflight.Checks();
    public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    public ISandboxExec Exec { get; } = SandboxFactory.Exec();
    public ISandboxUserSecrets UserSecrets { get; } = Substitute.For<ISandboxUserSecrets>();
    public string Location { get; }
    public string Name => SandboxName.For(Location);
    private readonly CliTestApplication application;
    public IServiceCollection Services => application.Services;
    public IApplicationPortPublisher Publisher => application.Publisher;
    public ServiceProvider Provider => application.Provider;

    public UserSecretsCliHarness(
        AboxConfig? config = null,
        bool unix = false,
        string? configYaml = null,
        bool existingSandbox = true
    )
    {
        Location = Root.CreateDirectory("folder");
        if (existingSandbox)
            ExistingSandbox();
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        application = new CliTestApplication(
            Console,
            AboxPaths.For(Root.Path, Root.Path),
            new TrailingArguments()
        );
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
        Services.AddSingleton(Sbx);
        Services.AddSingleton(Exec);
        Services.AddSingleton(UserSecrets);
        Services.AddSingleton(programs);
        Services.AddSingleton(Preflight);
    }

    public void ExistingSandbox(DateTimeOffset? createdAt = null) =>
        Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
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
        application.Dispose();
        Root.Dispose();
    }
}
