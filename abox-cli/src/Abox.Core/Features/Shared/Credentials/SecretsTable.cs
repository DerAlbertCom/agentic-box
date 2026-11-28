using Abox.Features.Shared.Common;
using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// The table of a squad's services and whether the host keeps them, as <c>abox secrets
/// list</c> prints it and the guided secrets setup ends with.
/// </summary>
internal static class SecretsTable
{
    public static void Write(IConsole console, SquadSecrets squad, HostSecrets stored)
    {
        var rows = squad.Services.Select(name => Row(name, squad, stored)).ToList();
        string[] header = ["SERVICE", "KITS", "NEED", "STATE", "NOTE"];
        var widths = header
            .Select(
                (title, column) => rows.Select(row => row[column].Length).Append(title.Length).Max()
            )
            .ToArray();
        foreach (var row in rows.Prepend(header))
        {
            console.WriteLine(
                string.Join(' ', row.Select((cell, column) => cell.PadRight(widths[column])))
                    .TrimEnd()
            );
        }
    }

    /// <summary>
    /// One row of the list. A custom secret the squad declares is required even when no
    /// kit asks for it: a kit's setup reads it from its variable.
    /// </summary>
    private static string[] Row(string name, SquadSecrets squad, HostSecrets stored)
    {
        var asked = squad.Request(name);
        var declaration = squad.Declaration(name);
        var kits = asked is null ? "-" : string.Join(", ", asked.Kits);
        var need =
            asked is { Optional: true } ? "optional"
            : asked is not null || declaration is { IsCustom: true } ? "required"
            : "-";
        var state = stored.Holds(declaration ?? new SecretDeclaration(name)) ? "stored" : "not set";
        var note =
            declaration is null ? "not declared"
            : stored.ConflictWith(declaration) is { } conflict ? "conflicts with " + Name(conflict)
            : "";
        return [name, kits, need, state, note];
    }

    /// <summary>How a stored custom secret is named: by its variable, else by its hosts.</summary>
    private static string Name(CustomSecret secret) =>
        secret.EnvironmentVariable ?? "the secret for " + string.Join(", ", secret.Targets);
}
