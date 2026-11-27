using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;

namespace Abox.Infrastructure.Sbx;

/// <summary>Preflight against sbx: on PATH, <c>sbx version</c>, <c>sbx diagnose</c> and <c>sbx login</c>.</summary>
internal sealed class SbxPreflightChecks(SbxInvoker invoker, IHostPrograms programs)
    : IPreflightChecks
{
    private const string AuthenticationCheck = "Authentication";
    private const string DiagnoseCommand = "sbx diagnose";

    public bool IsInstalled() => invoker.IsOnPath(programs);

    public async Task<RuntimeVersion> CheckVersionAsync(CancellationToken cancellationToken) =>
        new(await invoker.GetVersionAsync(cancellationToken), SbxInvoker.MinimumVersion);

    public async Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        // sbx diagnose exits non-zero when a check fails and still writes the whole
        // report, so only a failure without a report is an error.
        var result = await invoker.RunAsync(
            invoker.Call(["diagnose", "--json"]),
            cancellationToken
        );
        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new AboxException($"'{DiagnoseCommand}' failed: {SbxInvoker.Describe(result)}");
        }

        var report = SbxInvoker.Deserialize<SbxDiagnose>(result.StandardOutput, DiagnoseCommand);
        var checks = report
            .Checks.Select(check => new DiagnosticCheck(
                check.Name ?? string.Empty,
                (check.Status ?? string.Empty).Equals("fail", StringComparison.OrdinalIgnoreCase),
                check.Message ?? string.Empty,
                check.Detail ?? string.Empty,
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
