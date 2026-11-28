using Abox.Features.Shared.Common;

namespace Abox.Tests;

/// <summary>
/// A console that keeps what was written to it.
/// </summary>
public sealed class TestConsole : IConsole
{
    private readonly List<string> _output = [];
    private readonly List<string> _errors = [];

    public TestConsole(bool interactive = true, bool answer = true)
    {
        IsInteractive = interactive;
        Answer = answer;
    }

    public bool IsInteractive { get; }

    public bool Answer { get; set; }

    public IReadOnlyList<string> Output => _output;

    public IReadOnlyList<string> Errors => _errors;

    public string AllText => string.Join(Environment.NewLine, _output.Concat(_errors));

    public void WriteLine(string message = "") => _output.Add(message);

    public void WriteError(string message) => _errors.Add(message);

    public bool Confirm(string question)
    {
        _output.Add(question);
        return Answer;
    }

    /// <summary>The values the test hands out, in the order they are asked for.</summary>
    public List<string> Answers { get; } = [];

    /// <summary>The picks the test hands out, one per question, in order.</summary>
    public List<int> Picks { get; } = [];

    private int _next;

    private int _picked;

    public int? Choose(string question, IReadOnlyList<string> choices)
    {
        _output.Add(question);
        return _picked < Picks.Count ? Picks[_picked++] : null;
    }

    /// <summary>The lines the test hands out, in the order they are asked for.</summary>
    public List<string> Lines { get; } = [];

    private int _line;

    public string? ReadLine(string prompt)
    {
        _output.Add(prompt);
        return _line < Lines.Count ? Lines[_line++] : null;
    }

    /// <summary>
    /// The clear-text answers the test hands out, in order; an empty one takes the default.
    /// Without one left, the answer is empty.
    /// </summary>
    public List<string> Texts { get; } = [];

    private int _text;

    /// <summary>The picks of the multi-selects the test hands out, one per question, in order.</summary>
    public List<int[]> Selections { get; } = [];

    private int _selection;

    /// <summary>
    /// What the terminal echoed of the answers: the clear text of a text input and one
    /// <c>*</c> per character of a secret.
    /// </summary>
    public List<string> Echoes { get; } = [];

    public IReadOnlyList<int>? ChooseMany(string question, IReadOnlyList<string> choices)
    {
        _output.Add(question);
        _output.AddRange(choices.Select(choice => "  [ ] " + choice));
        return _selection < Selections.Count ? Selections[_selection++] : null;
    }

    public string? ReadText(string prompt, string? defaultValue)
    {
        _output.Add(
            string.IsNullOrEmpty(defaultValue) ? prompt : $"{prompt.TrimEnd()} [{defaultValue}] "
        );
        var answer = _text < Texts.Count ? Texts[_text++] : string.Empty;
        var value = answer.Length == 0 && defaultValue is not null ? defaultValue : answer;
        Echoes.Add(answer);
        return value;
    }

    public string? ReadSecret(string prompt)
    {
        _output.Add(prompt);
        var answer = _next < Answers.Count ? Answers[_next++] : string.Empty;
        Echoes.Add(new string('*', answer.Length));
        return answer;
    }
}
