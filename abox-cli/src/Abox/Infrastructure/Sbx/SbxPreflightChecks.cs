using Abox.Features.Shared.Preflight;

namespace Abox.Infrastructure.Sbx;

/// <summary>Preflight against sbx: on PATH, <c>sbx version</c>, <c>sbx diagnose</c> and <c>sbx login</c>.</summary>
public sealed class SbxPreflightChecks(SbxInvoker invoker, IHostPrograms programs)
    : IPreflightChecks
{
    private const string AuthenticationCheck = "Authentication";

    public bool IsInstalled() => invoker.IsOnPath(programs);

    public async Task<RuntimeVersion> CheckVersionAsync(CancellationToken cancellationToken) =>
        new(await invoker.GetVersionAsync(cancellationToken), SbxInvoker.MinimumVersion);

    public async Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var report = await invoker.RunJsonAsync<SbxDiagnose>(
            ["diagnose", "--json"],
            "sbx diagnose",
            cancellationToken
        );
        var checks = report
            .Checks.Select(check => new DiagnosticCheck(
                check.Name ?? string.Empty,
                (check.Status ?? string.Empty).Equals("fail", StringComparison.OrdinalIgnoreCase),
                check.Message ?? string.Empty,
                check.Hint ?? string.Empty
            ))
            .ToList();
        var signedOut = checks.Any(check =>
            check.Failed
            && check.Name.Equals(AuthenticationCheck, StringComparison.OrdinalIgnoreCase)
        );
        return new DiagnoseReport(checks, signedOut);
    }

    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        var result = await invoker.RunAsync(
            invoker.Call(["login"]) with
            {
                AttachConsole = true,
            },
            cancellationToken
        );
        return result.Succeeded;
    }
}
