using Abox.Cli;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal sealed class PortsCommandFixture : IDisposable
{
    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; } = new();
    public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    public IApplicationPortPublisher Publisher => application.Publisher;
    public ISandboxExec Exec { get; } = SandboxFactory.Exec();
    public string Location { get; }
    public string Name => SandboxName.For(Location);
    private readonly CliTestApplication application;

    public PortsCommandFixture(
        AboxConfig? config = null,
        IProcessRunner? runner = null,
        HostDevelopmentCertificate? exporter = null
    )
    {
        Location = Root.CreateDirectory("folder");
        Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        Sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(Name, "running", null) });
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        application = new CliTestApplication(
            Console,
            AboxPaths.For(Root.Path, Root.Path),
            new TrailingArguments()
        );
        application
            .Services.AddSingleton(config ?? new AboxConfig { DefaultLocation = Location })
            .AddSingleton<ISbxClient>(
                runner is null ? Sbx : new SbxClient(runner, AboxPaths.For(Root.Path, Root.Path))
            )
            .AddSingleton<ISandboxExec>(runner is null ? Exec : new SbxSandboxExec(runner))
            .AddSingleton(programs);
        if (runner is not null)
            application.Services.AddSingleton<IApplicationPortPublisher>(
                new SbxApplicationPorts(new SbxInvoker(runner, SbxClient.ExecutableName))
            );
        if (exporter is not null)
            application.Services.AddSingleton(exporter);
        _ = application.Provider;
    }

    public Task<int> Run(params string[] args) =>
        application.Provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["ports", .. args]);

    public Task<int> Run(CancellationToken cancellationToken, params string[] args) =>
        application
            .Provider.GetRequiredService<AboxRootCommand>()
            .Parse(["ports", .. args])
            .InvokeAsync(cancellationToken: cancellationToken);

    public async Task NoPublishing() =>
        await Publisher
            .DidNotReceive()
            .PublishAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );

    public void Dispose()
    {
        application.Dispose();
        Root.Dispose();
    }
}
