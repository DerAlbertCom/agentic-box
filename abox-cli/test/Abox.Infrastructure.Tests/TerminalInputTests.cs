using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class TerminalInputTests
{
    [Fact]
    public void AMaskedReadShowsOneStarPerTypedCharacter()
    {
        // Arrange
        var output = new StringWriter();

        // Act
        var value = TerminalInput.ReadMasked(Keys("tok€n", Enter), output);

        // Assert
        value.Should().Be("tok€n");
        output.ToString().Should().Be("*****" + Environment.NewLine);
    }

    [Fact]
    public void BackspaceRemovesTheLastCharacterAndItsStar()
    {
        // Arrange
        var output = new StringWriter();

        // Act
        var value = TerminalInput.ReadMasked(Keys("abc", Backspace, "d", Enter), output);

        // Assert
        value.Should().Be("abd");
        output.ToString().Should().Be("***\b \b*" + Environment.NewLine);
    }

    [Fact]
    public void BackspaceOnAnEmptyValueShowsNothing()
    {
        // Arrange
        var output = new StringWriter();

        // Act
        var value = TerminalInput.ReadMasked(Keys(Backspace, "x", Enter), output);

        // Assert
        value.Should().Be("x");
        output.ToString().Should().Be("*" + Environment.NewLine);
    }

    [Fact]
    public void APastedValueArrivesAsKeysAndEndsAtItsLineBreak()
    {
        // Arrange
        var output = new StringWriter();

        // Act
        var value = TerminalInput.ReadMasked(Keys("github_pat_1\rrest"), output);

        // Assert
        value.Should().Be("github_pat_1");
        output.ToString().Should().Be(new string('*', 12) + Environment.NewLine);
    }

    [Fact]
    public void AMultiSelectionMovesWithTheArrowsAndTogglesWithSpace()
    {
        // Arrange
        var selection = new MultiSelection(["jfrog", "elastic", "context7"]);

        // Act
        Press(selection, Down, Space, Down, Space, Up, Space, Space, Up, Up, Enter);

        // Assert
        selection.Picked.Should().Equal(1, 2);
        selection.Lines().Should().Equal("  [ ] jfrog", "  [x] elastic", "> [x] context7");
    }

    [Fact]
    public void AMultiSelectionStartsWithNothingPickedAndEscapeDropsThePicks()
    {
        // Arrange
        var selection = new MultiSelection(["jfrog", "elastic"]);
        selection.Picked.Should().BeEmpty();

        // Act
        var done = selection.Handle(Key(Space)) || selection.Handle(Key(Escape));

        // Assert
        done.Should().BeTrue();
        selection.Picked.Should().BeEmpty();
    }

    private const ConsoleKey Enter = ConsoleKey.Enter;
    private const ConsoleKey Backspace = ConsoleKey.Backspace;
    private const ConsoleKey Up = ConsoleKey.UpArrow;
    private const ConsoleKey Down = ConsoleKey.DownArrow;
    private const ConsoleKey Space = ConsoleKey.Spacebar;
    private const ConsoleKey Escape = ConsoleKey.Escape;

    private static void Press(MultiSelection selection, params ConsoleKey[] keys)
    {
        foreach (var key in keys)
        {
            if (selection.Handle(Key(key)))
            {
                return;
            }
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) =>
        new(key == ConsoleKey.Spacebar ? ' ' : '\0', key, false, false, false);

    /// <summary>Hands out the keys of text and special keys, in order.</summary>
    private static Func<ConsoleKeyInfo> Keys(params object[] parts)
    {
        var keys = new Queue<ConsoleKeyInfo>();
        foreach (var part in parts)
        {
            if (part is ConsoleKey special)
            {
                keys.Enqueue(Key(special));
                continue;
            }

            foreach (var character in (string)part)
            {
                keys.Enqueue(
                    character == '\r'
                        ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
                        : new ConsoleKeyInfo(character, default, false, false, false)
                );
            }
        }

        return keys.Dequeue;
    }
}
