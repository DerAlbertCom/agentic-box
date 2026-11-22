using Abox.Features.Shared.Preflight;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Preflight checks of an installed, supported, healthy and signed-in sandbox runtime.</summary>
internal static class ReadyPreflight
{
    public static RuntimeVersion SupportedVersion =>
        new(new Version(0, 51, 0), new Version(0, 51, 0));

    public static RuntimeVersion OldVersion => new(new Version(0, 50, 0), new Version(0, 51, 0));

    public static DiagnoseReport Healthy =>
        new(
            [
                new DiagnosticCheck("Authentication", false, "authenticated", ""),
                new DiagnosticCheck("Daemon", false, "healthy", ""),
            ],
            SignedOut: false
        );

    public static IPreflightChecks Checks()
    {
        var checks = Substitute.For<IPreflightChecks>();
        checks.IsInstalled().Returns(true);
        checks.CheckVersionAsync(Arg.Any<CancellationToken>()).Returns(SupportedVersion);
        checks.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(Healthy);
        return checks;
    }
}
