using Abox.Features.Shared.Preflight;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Synthetic sign-in recovery shared by Preflight and composed command tests.</summary>
internal sealed class PreflightScenario
{
    public PreflightScenario(bool interactive = true)
    {
        Console = new TestConsole(interactive);
        Checks.IsInstalled().Returns(true);
        Checks
            .CheckVersionAsync(Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                CommandToken = call.Arg<CancellationToken>();
                return ReadyPreflight.SupportedVersion;
            });
        Checks.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(SignedOut(), Healthy());
        Checks.SignInAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    public TestConsole Console { get; }
    public IPreflightChecks Checks { get; } = Substitute.For<IPreflightChecks>();
    public CancellationToken CommandToken { get; private set; }
    public Preflight Preflight => new(Console, Checks);

    public IEnumerable<string> Calls =>
        Checks.ReceivedCalls().Select(call => call.GetMethodInfo().Name);

    public static DiagnoseReport SignedOut() =>
        new(
            [
                new DiagnosticCheck(
                    "Authentication",
                    true,
                    "credential-sentinel: expired synthetic credential",
                    "credential-sentinel: synthetic detail",
                    "credential-sentinel: raw authentication diagnostic"
                ),
            ],
            SignedOut: true
        );

    public static DiagnoseReport Healthy() => ReadyPreflight.Healthy;
}
