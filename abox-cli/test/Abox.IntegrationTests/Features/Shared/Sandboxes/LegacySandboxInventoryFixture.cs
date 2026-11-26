using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal static class LegacySandboxInventoryFixture
{
    internal static List<ProcessRequest> UseRealSbx(
        UserSecretsCliHarness cli,
        Func<ProcessRequest, ProcessResult> inventory
    )
    {
        var requests = new List<ProcessRequest>();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                requests.Add(request);
                if (request.FileName == "bash")
                    return new ProcessResult(1, "", ""); // Missing physical fixture path: historical fallback.
                return request.Arguments[0] switch
                {
                    "version" => new ProcessResult(0, """{"client":{"version":"0.54.0"}}""", ""),
                    "diagnose" => new ProcessResult(0, """{"checks":[]}""", ""),
                    "ls" => inventory(request),
                    _ => new ProcessResult(0, "/legacy/workspace", ""),
                };
            });
        cli.Services.AddSingleton(runner);
        cli.Services.AddScoped<ISandboxLifecycle, SbxSandboxLifecycle>();
        cli.Services.AddScoped<IMcpServerRegistry, SbxMcpServerRegistry>();
        cli.Services.AddScoped<ISandboxInspection, SbxSandboxInspection>();
        return requests;
    }
}
