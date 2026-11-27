using Abox.Features.Shared.Common;

namespace Abox.Features.Shared.Preflight;

/// <summary>
/// The checks every command runs before it does anything: sbx is installed, it is new
/// enough for v3 kits, and it is healthy and signed in.
/// </summary>
internal sealed class Preflight(IConsole console, IPreflightChecks checks)
{
    /// <summary>
    /// Runs the checks. Returns false when a check failed and the command must not go on.
    /// </summary>
    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureInstalledAsync(cancellationToken))
        {
            return false;
        }

        if (!await EnsureVersionAsync(cancellationToken))
        {
            return false;
        }

        return await EnsureHealthyAsync(cancellationToken);
    }

    private async Task<bool> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (checks.IsInstalled())
        {
            return true;
        }

        console.WriteError("sbx is not on PATH. abox needs it and never installs anything itself.");
        foreach (var line in InstallInstructions.ForCurrentSystem())
        {
            console.WriteError(line);
        }

        return false;
    }

    private async Task<bool> EnsureVersionAsync(CancellationToken cancellationToken)
    {
        RuntimeVersion version;
        try
        {
            version = await checks.CheckVersionAsync(cancellationToken);
        }
        catch (AboxException exception)
        {
            console.WriteError(exception.Message);
            return false;
        }

        if (version.Supported)
        {
            return true;
        }

        console.WriteError(
            $"sbx {version.Installed} is too old. abox needs {version.Minimum} or later."
        );
        console.WriteError("Upgrade sbx and try again.");
        return false;
    }

    private async Task<bool> EnsureHealthyAsync(CancellationToken cancellationToken)
    {
        DiagnoseReport report;
        try
        {
            report = await checks.DiagnoseAsync(cancellationToken);
        }
        catch (AboxException exception)
        {
            console.WriteError(exception.Message);
            return false;
        }

        if (report.SignedOut)
        {
            if (!await SignInAsync(cancellationToken))
            {
                return false;
            }

            report = await checks.DiagnoseAsync(cancellationToken);
        }

        var failures = report.Failures;
        if (failures.Count == 0)
        {
            return true;
        }

        foreach (var failure in failures)
        {
            console.WriteError($"sbx check '{failure.Name}' failed: {failure.Message}");
            WriteErrorIfPresent(failure.Detail);
            WriteErrorIfPresent(failure.Hint);
        }

        return false;
    }

    private void WriteErrorIfPresent(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            console.WriteError(text);
        }
    }

    private async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        if (!console.IsInteractive)
        {
            console.WriteError("sbx is not signed in. Run 'sbx login' and try again.");
            return false;
        }

        console.WriteLine("Signing in to Docker...");

        var result = await checks.SignInAsync(cancellationToken);
        if (!result)
        {
            console.WriteError("Signing in failed. Run 'sbx login' and try again.");
            return false;
        }

        return true;
    }
}
