namespace Abox.Features.Shared.KitInspection;

/// <summary>
/// Checks the declarations of a local kit folder without building it, so a kit the
/// sandbox runtime would reject fails the build early.
/// </summary>
public interface IKitInspector
{
    /// <summary>Checks the kit in <paramref name="kitDirectory"/> with the given kit arguments.</summary>
    Task<KitInspectionOutcome> InspectAsync(
        string kitDirectory,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken
    );
}

/// <summary>Whether the kit's declarations were accepted.</summary>
/// <param name="Succeeded">Whether the kit's declarations were accepted.</param>
/// <param name="Output">What the check reported, so a rejection can be shown to the user.</param>
public sealed record KitInspectionOutcome(bool Succeeded, string Output);
