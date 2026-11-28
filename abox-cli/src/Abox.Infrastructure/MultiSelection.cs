namespace Abox.Infrastructure;

/// <summary>
/// The state of a multi-select list: the line the cursor is on and the picked lines. Up and
/// down move, space toggles, Enter ends, Escape ends without picks.
/// </summary>
internal sealed class MultiSelection(IReadOnlyList<string> choices)
{
    private readonly bool[] _picked = new bool[choices.Count];

    /// <summary>The line the cursor is on.</summary>
    public int Cursor { get; private set; }

    /// <summary>The picked lines, in list order.</summary>
    public IReadOnlyList<int> Picked =>
        Enumerable.Range(0, choices.Count).Where(index => _picked[index]).ToList();

    /// <summary>Handles one key. Returns true when the selection is done.</summary>
    public bool Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Cursor = (Cursor + choices.Count - 1) % choices.Count;
                return false;
            case ConsoleKey.DownArrow:
                Cursor = (Cursor + 1) % choices.Count;
                return false;
            case ConsoleKey.Spacebar:
                _picked[Cursor] = !_picked[Cursor];
                return false;
            case ConsoleKey.Escape:
                Array.Clear(_picked);
                return true;
            case ConsoleKey.Enter:
                return true;
            default:
                return false;
        }
    }

    /// <summary>The lines of the list as they are shown now.</summary>
    public IReadOnlyList<string> Lines() =>
        choices
            .Select(
                (choice, index) =>
                    $"{(index == Cursor ? '>' : ' ')} [{(_picked[index] ? 'x' : ' ')}] {choice}"
            )
            .ToList();
}
