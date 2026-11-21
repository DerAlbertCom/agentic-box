using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Abox.Infrastructure;

/// <summary>
/// <see cref="IProcessRunner"/> on top of <see cref="Process"/>.
/// </summary>
public sealed class ProcessRunner(ProcessOutput? output = null) : IProcessRunner
{
    private readonly Action<Process> kill = process => process.Kill(entireProcessTree: true);

    internal ProcessRunner(ProcessOutput? output, Action<Process> kill)
        : this(output) => this.kill = kill;

    public async Task<ProcessResult> RunUntilReadyAsync(
        ProcessRequest request,
        Func<CancellationToken, Task> confirmReadiness,
        CancellationToken cancellationToken
    )
    {
        // A released desktop cannot retain capture pipes owned by a short-lived CLI.
        // Make inheritance explicit, and never broaden sensitive-stream permissions.
        if (
            request.OutputPolicy != ProcessOutputPolicy.Normal
            || !request.AttachConsole
            || request.StandardInput is not null
            || request.OutputLines is not null
        )
            throw new AboxException(
                "Desktop startup requires normal inherited streams and no supplied stdin."
            );
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = StartInfo(request) };
        Start(process, request.FileName);
        try
        {
            return await ConfirmDesktopAsync(process, confirmReadiness, cancellationToken);
        }
        catch
        {
            await StopAsync(process);
            throw;
        }
    }

    private static async Task<ProcessResult> ConfirmDesktopAsync(
        Process process,
        Func<CancellationToken, Task> confirmReadiness,
        CancellationToken cancellationToken
    )
    {
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var exited = process.WaitForExitAsync(observation.Token);
        try
        {
            var readiness = confirmReadiness(observation.Token);
            var completed = await Task.WhenAny(exited, readiness);
            if (completed == exited && process.HasExited && process.ExitCode != 0)
            {
                observation.Cancel();
                await JoinCancelledObservationAsync(readiness, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new ProcessResult(process.ExitCode, "", "");
            }
            await readiness;
            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessResult(process.HasExited ? process.ExitCode : 0, "", "");
        }
        finally
        {
            // Cancel only the exit observer, not the healthy desktop. No background
            // wait or capture task is left behind when ownership transfers to the user.
            observation.Cancel();
            await JoinCancelledObservationAsync(exited, cancellationToken);
        }
    }

    private static async Task JoinCancelledObservationAsync(
        Task observation,
        CancellationToken caller
    )
    {
        try
        {
            await observation;
        }
        catch (OperationCanceledException)
        {
            // Stopping our observer is not cancellation of the user's desktop;
            // explicit caller cancellation still takes precedence.
            caller.ThrowIfCancellationRequested();
        }
    }

    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (request.OutputPolicy != ProcessOutputPolicy.Normal && request.AttachConsole)
                throw new AboxException("Sensitive processes cannot attach console streams.");
            EnsureStreamable(request);
            return await RunCoreAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (request.OutputPolicy != ProcessOutputPolicy.Normal)
        {
            // Even exceptions can contain fragments of a payload or child diagnostics.
            throw new AboxException(
                "Sensitive process failed. Check the runtime and destination permissions; child diagnostics were suppressed."
            );
        }
    }

    private async Task<ProcessResult> RunCoreAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = StartInfo(request) };
        var capture = new OutputCapture(
            request.OutputPolicy == ProcessOutputPolicy.Sensitive,
            request.OutputLines
        );
        if (!request.AttachConsole)
        {
            process.OutputDataReceived += (_, e) =>
                capture.Add(ProcessStream.StandardOutput, e.Data);
            process.ErrorDataReceived += (_, e) => capture.Add(ProcessStream.StandardError, e.Data);
        }

        Start(process, request.FileName);
        await CompleteAsync(process, request, cancellationToken);

        var result = capture.Result(process.ExitCode);
        output?.Write(request, result);
        return result;
    }

    private static void EnsureStreamable(ProcessRequest request)
    {
        if (
            request.OutputLines is not null
            && (request.OutputPolicy != ProcessOutputPolicy.Normal || request.AttachConsole)
        )
            throw new AboxException(
                "Only normal captured processes can stream their output lines."
            );
    }

    /// <summary>Both streams are read on their own threads; one lock keeps their arrival order.</summary>
    private sealed class OutputCapture(bool discard, Action<ProcessOutputLine>? lines)
    {
        private readonly Lock gate = new();
        private readonly StringBuilder standardOutput = new();
        private readonly StringBuilder standardError = new();
        private readonly StringBuilder combined = new();

        public void Add(ProcessStream stream, string? text)
        {
            if (discard || text is null)
                return;

            lock (gate)
            {
                (
                    stream == ProcessStream.StandardOutput ? standardOutput : standardError
                ).AppendLine(text);
                combined.AppendLine(text);
                lines?.Invoke(new ProcessOutputLine(stream, text));
            }
        }

        public ProcessResult Result(int exitCode)
        {
            lock (gate)
            {
                return new ProcessResult(
                    exitCode,
                    standardOutput.ToString(),
                    standardError.ToString()
                )
                {
                    Output = combined.ToString(),
                };
            }
        }
    }

    private static ProcessStartInfo StartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (name, value) in request.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        if (!request.AttachConsole)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
        }

        if (request.StandardInput is not null)
        {
            startInfo.RedirectStandardInput = true;
            if (
                request.UseUtf8StandardInput
                || request.OutputPolicy == ProcessOutputPolicy.Sensitive
            )
                startInfo.StandardInputEncoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true
                );
        }

        return startInfo;
    }

    private static void Start(Process process, string fileName)
    {
        try
        {
            process.Start();
        }
        catch (Exception exception)
        {
            throw new AboxException($"Cannot start '{fileName}'. {exception.Message}", exception);
        }
    }

    private async Task CompleteAsync(
        Process process,
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (!request.AttachConsole)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            if (request.StandardInput is not null)
                await WriteInputAsync(
                    process.StandardInput,
                    request.StandardInput,
                    cancellationToken
                );

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation during input or exit must not leave children or their workers running.
            await StopAsync(process);
            throw;
        }
    }

    private static async Task WriteInputAsync(
        StreamWriter input,
        string value,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await input.WriteAsync(value.AsMemory(), cancellationToken);
            await input.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // A child can reject input early. Drain its output and await its exit normally.
            cancellationToken.ThrowIfCancellationRequested();
        }

        try
        {
            input.Close();
        }
        catch (IOException)
        {
            // Closing flushes any buffered input, so it can observe the same broken pipe.
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task StopAsync(Process process)
    {
        try
        {
            kill(process);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or Win32Exception
                        or AggregateException
                        or OperationCanceledException
            )
        {
            // Cleanup is best effort; preserve the original failure even if children survive.
        }
    }
}
