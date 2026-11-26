using System.CommandLine;
using Abox.Cli;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Dotnet;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal sealed class PortsCommandFixture : IDisposable
{
    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; } = new();
    public IPreflightChecks Preflight { get; } = ReadyPreflight.Checks();
    public IApplicationPortPublisher Publisher => _application.Publisher;
    public ISandboxDevelopmentCertificates Certificates => _application.Certificates;
    public ISandboxExec Exec { get; } = SandboxFactory.Exec();
    public string Location { get; }
    public string Name => SandboxName.For(Location);
    private readonly CliTestApplication _application;
    public ISandboxLifecycle Lifecycle => _application.Lifecycle;

    public PortsCommandFixture(
        AboxConfig? config = null,
        IProcessRunner? runner = null,
        DotnetDevelopmentCertificate? exporter = null
    )
    {
        Location = Root.CreateDirectory("folder");
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        _application = new CliTestApplication(
            Console,
            AboxPaths.For(Root.Path, Root.Path),
            new TrailingArguments()
        );
        _application
            .Services.AddSingleton(config ?? new AboxConfig { DefaultLocation = Location })
            .AddSingleton<ISandboxExec>(
                runner is null ? Exec : new SbxSandboxExec(new SbxInvoker(runner))
            )
            .AddSingleton(programs)
            .AddSingleton(Preflight);
        if (runner is null)
        {
            _application.WithSandboxSubstitutes();
            Lifecycle
                .ListAsync(Arg.Any<CancellationToken>())
                .Returns(new[] { new SandboxInfo(Name, "running", null) });
        }
        else
        {
            _application.Services.AddSingleton<ISandboxLifecycle>(
                new SbxSandboxLifecycle(new SbxInvoker(runner))
            );
            _application
                .Services.AddSingleton<IApplicationPortPublisher>(
                    new SbxApplicationPorts(new SbxInvoker(runner))
                )
                .AddSingleton<ISandboxDevelopmentCertificates>(
                    new SbxDevelopmentCertificates(new SbxInvoker(runner))
                );
        }
        if (exporter is not null)
            _application.Services.AddSingleton<IHostDevelopmentCertificate>(exporter);
        _ = _application.Provider;
    }

    public Task<int> Run(params string[] args) =>
        _application.Provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["ports", .. args]);

    public Task<int> Run(CancellationToken cancellationToken, params string[] args) =>
        _application
            .Provider.GetRequiredService<AboxRootCommand>()
            .Parse(["ports", .. args])
            .InvokeAsync(cancellationToken: cancellationToken);

    /// <summary>Shows the help of <c>ports</c> and returns it as written.</summary>
    public async Task<string> Help()
    {
        using var output = new StringWriter();
        await _application
            .Provider.GetRequiredService<AboxRootCommand>()
            .Parse(["ports", "--help"])
            .InvokeAsync(new InvocationConfiguration { Output = output });
        return output.ToString();
    }

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
        _application.Dispose();
        Root.Dispose();
    }
}
