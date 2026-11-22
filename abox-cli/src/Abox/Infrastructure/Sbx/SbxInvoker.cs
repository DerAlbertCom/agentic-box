using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abox.Infrastructure.Sbx;

internal sealed partial class SbxInvoker(IProcessRunner runner)
{
    internal const string Executable = "sbx";

    internal string ExecutableName => Executable;

    /// <summary>The first sbx version abox works with.</summary>
    internal static readonly Version MinimumVersion = new(0, 51, 0);

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
        var result = await RunOrThrowAsync(
            new ProcessRequest(ExecutableName, arguments),
            command,
            cancellationToken
        );
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

    /// <summary>Reads the sbx version, like <c>v0.51.0</c> or <c>0.51.0</c>.</summary>
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
}
