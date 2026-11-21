using System.Text.RegularExpressions;

namespace Abox.Sbx;

/// <summary>
/// Turns the output of a failed sbx call into the few lines that name its cause, so a
/// failure does not have to be searched for in a long dump.
/// </summary>
public static partial class SbxFailureHeadline
{
    /// <summary>
    /// The first failed kit step with its captured output, or else the last <c>error:</c>
    /// line, or else the last line sbx wrote.
    /// </summary>
    public static string Describe(SbxOutcome outcome)
    {
        var lines = outcome
            .Output.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();
        return FailedStep(lines)
            ?? LastErrorLine(lines)
            ?? lines.LastOrDefault(line => line.Length > 0)
            ?? $"sbx exited with code {outcome.ExitCode}.";
    }

    private static string? FailedStep(List<string> lines)
    {
        var index = lines.FindIndex(line => FailedStepMarker().IsMatch(line));
        if (index < 0)
            return null;

        var marker = FailedStepMarker().Match(lines[index]);
        var headline = new List<string>
        {
            $"The kit '{marker.Groups["kit"].Value}' failed (exit {marker.Groups["exit"].Value}): {marker.Groups["step"].Value}",
        };
        if (index + 1 < lines.Count && CapturedOutputHeader().IsMatch(lines[index + 1]))
            headline.AddRange(CapturedLines(lines, index + 2).Select(line => "    " + line));
        return string.Join(Environment.NewLine, headline);
    }

    private static List<string> CapturedLines(List<string> lines, int start)
    {
        var captured = lines.Skip(start).TakeWhile(line => !EndsCapturedOutput(line)).ToList();
        while (captured.Count > 0 && captured[^1].Length == 0)
            captured.RemoveAt(captured.Count - 1);
        return captured;
    }

    private static bool EndsCapturedOutput(string line) =>
        Rule().IsMatch(line)
        || StepMarker().IsMatch(line)
        || line.StartsWith("error:", StringComparison.Ordinal)
        || line.StartsWith("WARN:", StringComparison.Ordinal);

    private static string? LastErrorLine(List<string> lines) =>
        lines.LastOrDefault(line => line.StartsWith("error:", StringComparison.Ordinal));

    [GeneratedRegex(
        @"^✗\s+(?<step>.+?)\s+\(kit=(?<kit>[^,)]+)(?:,[^)]*)?,\s*exit (?<exit>\d+)\)$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex FailedStepMarker();

    [GeneratedRegex(@"^[✗✓$+]\s.*\(kit=", RegexOptions.CultureInvariant)]
    private static partial Regex StepMarker();

    [GeneratedRegex(@"^─+\s*captured output\s*─*$", RegexOptions.CultureInvariant)]
    private static partial Regex CapturedOutputHeader();

    [GeneratedRegex(@"^─+$", RegexOptions.CultureInvariant)]
    private static partial Regex Rule();
}
