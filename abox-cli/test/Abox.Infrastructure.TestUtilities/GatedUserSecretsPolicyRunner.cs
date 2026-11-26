using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Abox.Features.Shared.Configuration;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using AwesomeAssertions;

namespace Abox.Tests;

internal sealed class GatedUserSecretsPolicyRunner : IProcessRunner, IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly Channel<int> _started = Channel.CreateUnbounded<int>();
    private readonly Channel<int> _cancelled = Channel.CreateUnbounded<int>();
    private readonly Channel<int> _finished = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ProcessResult>> _checks = new();
    private readonly TaskCompletionSource _cleanup = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly string _inventory;
    private int _active;
    private int _maximumActive;

    internal GatedUserSecretsPolicyRunner(string sandbox, params string[] resources)
    {
        Sandbox = sandbox;
        _inventory = Inventory(sandbox, resources);
        Client = new SbxUserSecrets(new SbxInvoker(this));
    }

    internal string Sandbox { get; }
    internal SbxUserSecrets Client { get; }
    internal bool HoldCancellationCleanup { get; init; }
    internal ConcurrentQueue<ProcessRequest> Requests { get; } = new();
    internal int Active => Volatile.Read(ref _active);
    internal int MaximumActive => Volatile.Read(ref _maximumActive);

    internal Task<IReadOnlyList<int>> ReadAsync(CancellationToken token = default) =>
        Client.GetUserSecretsHostPortsAsync(Sandbox, token);

    internal async Task<int> NextStartedAsync() =>
        await _started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal async Task<int> NextCancelledAsync() =>
        await _cancelled.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal async Task<int> NextFinishedAsync() =>
        await _finished.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal void Complete(int port, bool allowed = true) =>
        CompleteRaw(port, new ProcessResult(0, Authorization(Sandbox, port, allowed), ""));

    internal void CompleteRaw(int port, ProcessResult result) => _checks[port].SetResult(result);

    internal void Fail(int port) =>
        _checks[port]
            .SetException(new InvalidOperationException("synthetic-password 123456789 true"));

    internal void ReleaseCleanup() => _cleanup.TrySetResult();

    public Task<ProcessResult> RunUntilReadyAsync(
        ProcessRequest request,
        Func<CancellationToken, Task> confirmReadiness,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("Policy fixtures must not start a desktop.");

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
    {
        Requests.Enqueue(request);
        if (request.Arguments[1] == "ls")
            return new ProcessResult(0, _inventory, "");

        var port = int.Parse(request.Arguments[^1].AsSpan("localhost:".Length));
        var gate = new TaskCompletionSource<ProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _checks.TryAdd(port, gate).Should().BeTrue("each normalized candidate is checked once");
        var count = Interlocked.Increment(ref _active);
        int previous;
        do
        {
            previous = Volatile.Read(ref _maximumActive);
        } while (
            count > previous
            && Interlocked.CompareExchange(ref _maximumActive, count, previous) != previous
        );
        _started.Writer.TryWrite(port);
        try
        {
            return await gate.Task.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            _cancelled.Writer.TryWrite(port);
            if (HoldCancellationCleanup)
                await _cleanup.Task;
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
            _finished.Writer.TryWrite(port);
        }
    }

    internal static string Inventory(string sandbox, params string[] resources) =>
        JsonSerializer.Serialize(
            new
            {
                rules = new[]
                {
                    new
                    {
                        name = "kit:" + sandbox,
                        scope = "sandbox:" + sandbox,
                        applies_to = "sandbox:" + sandbox,
                        resource_type = "network",
                        decision = "allow",
                        origin = "scoped",
                        layer = "local",
                        status = "active",
                        editable = false,
                        sandbox_id = sandbox,
                        provenance = new { created_via = "provisioned", source = "sandboxes" },
                        actions = new[] { "net:connect:tcp" },
                        resources,
                    },
                },
            }
        );

    internal static string Authorization(string sandbox, int port, bool allowed = true) =>
        JsonSerializer.Serialize(
            new
            {
                action = "net:connect:tcp",
                allowed,
                context = "sandbox:" + sandbox,
                resource_type = "net:domain",
                resource_value = "localhost:" + port,
                target = "localhost:" + port,
                type = "network",
            }
        );

    public void Dispose()
    {
        ReleaseCleanup();
        _root.Dispose();
    }
}
