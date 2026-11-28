using Abox.Features.Shared.Common;

namespace Abox.Infrastructure;

/// <summary>
/// <see cref="IConsole"/> on top of <see cref="System.Console"/>. Redirected input is read
/// line by line, without masking and without a list to move through.
/// </summary>
internal sealed class SystemConsole : IConsole
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public void WriteLine(string message = "") => Console.Out.WriteLine(message);

    public void WriteError(string message) => Console.Error.WriteLine(message);

    public bool Confirm(string question)
    {
        Console.Out.Write($"{question} [y/N] ");
        var answer = Console.In.ReadLine();
        return answer is not null
            && (
                answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            );
    }

    public int? Choose(string question, IReadOnlyList<string> choices)
    {
        Console.Out.WriteLine(question);
        WriteNumbered(choices);
        Console.Out.Write("Number? ");
        var answer = Console.ReadLine()?.Trim();
        if (!int.TryParse(answer, out var pick) || pick < 1 || pick > choices.Count)
        {
            return null;
        }

        return pick - 1;
    }

    public IReadOnlyList<int>? ChooseMany(string question, IReadOnlyList<string> choices)
    {
        Console.Out.WriteLine(question);
        if (choices.Count == 0)
        {
            return [];
        }

        return Console.IsInputRedirected
            ? ChooseManyByNumbers(choices)
            : ChooseManyWithKeys(choices);
    }

    /// <summary>The picks as numbers on one line, such as <c>1,3</c>.</summary>
    private static IReadOnlyList<int>? ChooseManyByNumbers(IReadOnlyList<string> choices)
    {
        WriteNumbered(choices);
        Console.Out.Write("Numbers, separated by commas? ");
        var answer = Console.In.ReadLine();
        if (answer is null)
        {
            return null;
        }

        return answer
            .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var pick) ? pick - 1 : -1)
            .Where(pick => pick >= 0 && pick < choices.Count)
            .Distinct()
            .Order()
            .ToList();
    }

    private static IReadOnlyList<int> ChooseManyWithKeys(IReadOnlyList<string> choices)
    {
        Console.Out.WriteLine("  (up and down to move, space to pick, Enter to confirm)");
        var selection = new MultiSelection(choices);
        Render(selection);
        while (!selection.Handle(Console.ReadKey(intercept: true)))
        {
            // Back to the first line of the list, to draw it over itself.
            Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - choices.Count));
            Render(selection);
        }

        return selection.Picked;
    }

    private static void WriteNumbered(IReadOnlyList<string> choices)
    {
        for (var index = 0; index < choices.Count; index++)
        {
            Console.Out.WriteLine($"  {index + 1}. {choices[index]}");
        }
    }

    private static void Render(MultiSelection selection)
    {
        var lines = selection.Lines();
        var width = Math.Max(1, Console.BufferWidth - 1);
        foreach (var line in lines)
        {
            var shown = line.Length > width ? line[..width] : line;
            Console.Out.WriteLine(shown.PadRight(width));
        }
    }

    public string? ReadLine(string prompt)
    {
        Console.Out.Write(prompt);
        return Console.In.ReadLine();
    }

    public string? ReadText(string prompt, string? defaultValue)
    {
        Console.Out.Write(
            string.IsNullOrEmpty(defaultValue) ? prompt : $"{prompt.TrimEnd()} [{defaultValue}] "
        );
        var answer = Console.In.ReadLine();
        if (answer is null)
        {
            return null;
        }

        return answer.Length == 0 && defaultValue is not null ? defaultValue : answer;
    }

    public string? ReadSecret(string prompt)
    {
        Console.Out.Write(prompt);

        if (Console.IsInputRedirected)
        {
            var line = Console.In.ReadLine();
            Console.Out.WriteLine();
            return line;
        }

        return TerminalInput.ReadMasked(() => Console.ReadKey(intercept: true), Console.Out);
    }
}
