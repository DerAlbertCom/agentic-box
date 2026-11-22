namespace Abox.Features.Shared.Preflight;

/// <summary>The health of the sandbox runtime, as its own diagnosis reports it.</summary>
/// <param name="Checks">Every check of the diagnosis.</param>
/// <param name="SignedOut">The diagnosis found that nobody is signed in.</param>
public sealed record DiagnoseReport(IReadOnlyList<DiagnosticCheck> Checks, bool SignedOut)
{
    public IReadOnlyList<DiagnosticCheck> Failures => Checks.Where(check => check.Failed).ToList();
}
