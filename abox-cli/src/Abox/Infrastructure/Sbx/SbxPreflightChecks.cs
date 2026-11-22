using Abox.Features.Shared.Preflight;

namespace Abox.Infrastructure.Sbx;

/// <summary>Preflight against sbx: on PATH, <c>sbx version</c>, <c>sbx diagnose</c> and <c>sbx login</c>.</summary>
public sealed class SbxPreflightChecks(IProcessRunner runner, IHostPrograms programs)
    : IPreflightChecks
{
    private const string AuthenticationCheck = "Authentication";
    private readonly SbxInvoker _invoker = new(runner);

    public bool IsInstalled() => programs.Exists(SbxInvoker.Executable);

    public async Task<RuntimeVersion> CheckVersionAsync(CancellationToken cancellationToken) =>
        new(await _invoker.GetVersionAsync(cancellationToken), SbxInvoker.MinimumVersion);

    public async Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var report = await _invoker.RunJsonAsync<SbxDiagnose>(
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
        var result = await _invoker.RunAsync(
            new ProcessRequest(SbxInvoker.Executable, ["login"], AttachConsole: true),
            cancellationToken
        );
        return result.Succeeded;
    }
}
