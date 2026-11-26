using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Preflight;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// The one way to call <c>sbx</c>: owns the executable name, the minimum version,
/// captured execution, JSON handling and the failure headline. Every sbx adapter
/// calls sbx through it.
/// </summary>
internal sealed partial class SbxInvoker(IProcessRunner runner)
{
    private const string Executable = "sbx";

    /// <summary>The first sbx version abox works with.</summary>
    internal static readonly Version MinimumVersion = new(0, 54, 0);

    /// <summary>Whether sbx is on the PATH of the host.</summary>
    internal bool IsOnPath(IHostPrograms programs) => programs.Exists(Executable);

    /// <summary>A call of sbx with these arguments; callers add what else it needs with <c>with</c>.</summary>
    internal ProcessRequest Call(IReadOnlyList<string> arguments) => new(Executable, arguments);

    internal Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    ) => runner.RunAsync(request, cancellationToken);

    internal async Task<SbxOutcome> RunOutcomeAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(request, cancellationToken);
        return new SbxOutcome(result.ExitCode, result.Output);
    }

    internal async Task<ProcessResult> RunOrThrowAsync(
        ProcessRequest request,
        string command,
        CancellationToken cancellationToken
    )
    {
        var result = await RunAsync(request, cancellationToken);
        if (!result.Succeeded)
            throw new AboxException($"'{command}' failed: {Describe(result)}");
        return result;
    }

    internal async Task<T> RunJsonAsync<T>(
        IReadOnlyList<string> arguments,
        string command,
        CancellationToken cancellationToken
    )
        where T : class
    {
        var result = await RunOrThrowAsync(Call(arguments), command, cancellationToken);
        return Deserialize<T>(result.StandardOutput, command);
    }

    internal static T Deserialize<T>(string json, string command)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new AboxException($"'{command}' wrote no output.");
        try
        {
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new AboxException($"'{command}' wrote no output.");
        }
        catch (JsonException exception)
        {
            throw new AboxException(
                $"Cannot read the output of '{command}': {exception.Message}",
                exception
            );
        }
    }

    internal static string Describe(ProcessResult result)
    {
        var text = (result.StandardError + result.StandardOutput).Trim();
        return string.IsNullOrEmpty(text) ? $"exit code {result.ExitCode}" : text;
    }

    /// <summary>The version of the installed sbx client.</summary>
    internal async Task<Version> GetVersionAsync(CancellationToken cancellationToken)
    {
        // The JSON format is read the same way as every other call, so the version
        // does not depend on the words of the plain text.
        var report = await RunJsonAsync<SbxVersion>(
            ["version", "--json"],
            "sbx version",
            cancellationToken
        );
        return ParseVersion(report.Client?.Version ?? string.Empty);
    }

    /// <summary>Reads the sbx version, like <c>v0.54.0</c> or <c>0.54.0</c>.</summary>
    private static Version ParseVersion(string text)
    {
        var match = VersionPattern().Match(text);
        if (!match.Success)
        {
            throw new AboxException($"Cannot read the sbx version from '{text}'.");
        }

        return new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value)
        );
    }

    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionPattern();

    /// <summary>
    /// Turns the output of a failed sbx call into the few lines that name its cause, so a
    /// failure does not have to be searched for in a long dump: the first failed kit step
    /// with its captured output, or else the last <c>error:</c> line, or else the last line sbx wrote.
    /// </summary>
    internal static string FailureHeadline(SbxOutcome outcome)
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
