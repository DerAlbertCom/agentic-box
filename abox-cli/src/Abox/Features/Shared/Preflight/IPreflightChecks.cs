namespace Abox.Features.Shared.Preflight;

/// <summary>
/// What preflight asks of the sandbox runtime: is it installed, new enough, healthy and
/// signed in, and the sign-in itself.
/// </summary>
public interface IPreflightChecks
{
    bool IsInstalled();

    /// <summary>The installed version against the minimum; an unreadable version throws <see cref="AboxException"/>.</summary>
    Task<RuntimeVersion> CheckVersionAsync(CancellationToken cancellationToken);

    Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken);

    /// <summary>Starts the interactive browser sign-in. Returns false when it failed.</summary>
    Task<bool> SignInAsync(CancellationToken cancellationToken);
}
