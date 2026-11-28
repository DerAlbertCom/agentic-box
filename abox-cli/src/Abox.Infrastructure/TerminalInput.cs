using System.Text;

namespace Abox.Infrastructure;

/// <summary>
/// The key-by-key input of a terminal: a masked read. Kept
/// apart from <see cref="SystemConsole"/> so it can be tested with scripted keys.
/// </summary>
internal static class TerminalInput
{
    /// <summary>
    /// Reads a value up to Enter and writes one <c>*</c> per character, so typed and pasted
    /// input is seen to arrive. Backspace removes the last character and its <c>*</c>.
    /// </summary>
    public static string ReadMasked(Func<ConsoleKeyInfo> readKey, TextWriter output)
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = readKey();
            if (key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n')
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                Erase(value, output);
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
                output.Write('*');
            }
        }

        output.WriteLine();
        return value.ToString();
    }

    /// <summary>Removes the last character and its <c>*</c>; nothing when the value is empty.</summary>
    private static void Erase(StringBuilder value, TextWriter output)
    {
        if (value.Length == 0)
        {
            return;
        }

        value.Remove(value.Length - 1, 1);
        output.Write("\b \b");
    }
}
