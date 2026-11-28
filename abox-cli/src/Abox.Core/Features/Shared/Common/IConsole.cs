namespace Abox.Features.Shared.Common;

/// <summary>
/// The terminal abox talks to. Abstracted so commands can be tested without a console.
/// </summary>
public interface IConsole
{
    /// <summary>True when a human can answer a question.</summary>
    bool IsInteractive { get; }

    void WriteLine(string message = "");

    void WriteError(string message);

    /// <summary>Asks a yes/no question. Returns false when the answer is no or unreadable.</summary>
    bool Confirm(string question);

    /// <summary>
    /// Asks the user to pick one of the values, numbered. Returns the index of the pick,
    /// or null when nothing was picked.
    /// </summary>
    int? Choose(string question, IReadOnlyList<string> choices);

    /// <summary>
    /// Asks the user to pick any of the values: on a terminal with the arrow keys and the
    /// space bar, nothing preselected. Returns the indexes of the picks in list order, empty
    /// when nothing was picked, or null when there is nothing to read.
    /// </summary>
    IReadOnlyList<int>? ChooseMany(string question, IReadOnlyList<string> choices);

    /// <summary>
    /// Asks for one line of text. Returns null when there is nothing to read.
    /// </summary>
    string? ReadLine(string prompt);

    /// <summary>
    /// Asks for one line of text the user sees while typing. An empty answer takes the
    /// default, when there is one. Returns null when there is nothing to read.
    /// </summary>
    string? ReadText(string prompt, string? defaultValue);

    /// <summary>
    /// Reads a value the user should not see, for example a secret: a terminal shows one
    /// <c>*</c> per character. Returns null when there is nothing to read.
    /// </summary>
    string? ReadSecret(string prompt);
}
