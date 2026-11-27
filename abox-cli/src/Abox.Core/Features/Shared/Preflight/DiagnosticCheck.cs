namespace Abox.Features.Shared.Preflight;

/// <summary>One check of the sandbox runtime's diagnosis.</summary>
/// <param name="Name">The name of the check.</param>
/// <param name="Failed">The check found a problem.</param>
/// <param name="Message">What the check found.</param>
/// <param name="Detail">The particulars behind the message, such as the underlying error.</param>
/// <param name="Hint">What to do about it, when the check failed.</param>
/// <param name="TimedOut">
/// The runtime gave up on the check before it finished, so it found nothing either way.
/// </param>
public sealed record DiagnosticCheck(
    string Name,
    bool Failed,
    string Message,
    string Detail,
    string Hint,
    bool TimedOut = false
);
