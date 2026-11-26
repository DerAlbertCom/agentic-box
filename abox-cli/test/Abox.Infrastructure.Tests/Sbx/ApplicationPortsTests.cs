using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class ApplicationPortsTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly List<ProcessRequest> _calls = [];
    private string _inventory = "[]";
    private ProcessResult? _publishFailure;
    private bool _acknowledgeWithoutPublishing;
    private string? _publicationInventory;
    private string _relayInventory = "[{\"application_port\":8203,\"relay_port\":58203}]";
    private ProcessResult? _relayFailure;
    private readonly SbxApplicationPorts _client;

    public ApplicationPortsTests()
    {
        _client = new SbxApplicationPorts(new SbxInvoker(_runner));
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => RespondToRuntime(call.Arg<ProcessRequest>()));
    }

    private ProcessResult RespondToRuntime(ProcessRequest request)
    {
        _calls.Add(request);
        if (request.Arguments.Contains("python3") && _relayFailure is not null)
            return _relayFailure;
        if (request.Arguments.Contains("--unpublish"))
            _inventory =
                "[{\"host_ip\":\"127.0.0.1\",\"host_port\":19000,\"sandbox_port\":9000,\"protocol\":\"tcp4\"}]";
        if (request.Arguments.Contains("--publish") && _publishFailure is not null)
            return _publishFailure;
        if (request.Arguments.Contains("--publish") && !_acknowledgeWithoutPublishing)
            _inventory =
                _publicationInventory
                ?? """
                    [{"host_ip":"127.0.0.1","host_port":18203,"sandbox_port":58203,"protocol":"tcp4"},
                     {"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
                    """;
        return new ProcessResult(
            0,
            request.Arguments.Contains("python3") ? _relayInventory
                : request.Arguments[0] == "ports" ? _inventory
                : "",
            ""
        );
    }

    [Fact]
    public async Task DifferentOffsetsInOnePlanAreRejectedBeforeRuntimeMutation()
    {
        // Arrange
        var mappings = new ApplicationPortMapping[] { new(8203, 18203), new(8204, 28204) };

        // Act
        var action = () => _client.PublishAsync("actual", mappings, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*same offset*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RelayReceivesTheOffsetAndCompleteTcpInventoryBeforeAnyBindingChange()
    {
        // Arrange
        _inventory = """
            [{"host_ip":"127.0.0.1","host_port":28203,"sandbox_port":58203,"protocol":"tcp4"},
             {"host_ip":"0.0.0.0","host_port":19000,"sandbox_port":59000,"protocol":"tcp"},
             {"host_ip":"::1","host_port":19001,"sandbox_port":59001,"protocol":"tcp6"}]
            """;

        // Act
        await _client.PublishAsync("actual", [new(8203, 28203)], CancellationToken.None);

        // Assert
        var relay = _calls.Single(call => call.Arguments.Contains("python3"));
        relay
            .Arguments.Should()
            .ContainInOrder(
                "--offset",
                "20000",
                "--bindings",
                "[[58203,28203],[59000,19000],[59001,19001]]"
            );
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--publish") || call.Arguments.Contains("--unpublish")
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterPartialPublicationOrCancellationOnlyPublishesTheMissingBinding(
        bool cancel
    )
    {
        // Arrange
        _relayInventory =
            """[{"application_port":8203,"relay_port":58203},{"application_port":8204,"relay_port":58204}]""";
        var attempts = 0;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--publish")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _calls.Add(call.Arg<ProcessRequest>());
                attempts++;
                _inventory =
                    attempts == 1
                        ? """[{"host_ip":"127.0.0.1","host_port":28203,"sandbox_port":58203,"protocol":"tcp4"}]"""
                        : """[{"host_ip":"127.0.0.1","host_port":28203,"sandbox_port":58203,"protocol":"tcp4"},{"host_ip":"127.0.0.1","host_port":28204,"sandbox_port":58204,"protocol":"tcp4"}]""";
                return attempts == 1 && cancel
                    ? Task.FromException<ProcessResult>(new OperationCanceledException())
                    : Task.FromResult(
                        new ProcessResult(
                            attempts == 1 ? 1 : 0,
                            _inventory,
                            attempts == 1 ? "partial runtime failure" : ""
                        )
                    );
            });
        var mappings = new ApplicationPortMapping[] { new(8203, 28203), new(8204, 28204) };

        // Act
        var first = () => _client.PublishAsync("actual", mappings, CancellationToken.None);
        if (cancel)
            await first.Should().ThrowAsync<OperationCanceledException>();
        else
            await first
                .Should()
                .ThrowAsync<AboxException>()
                .WithMessage("*partial runtime failure*");
        await _client.PublishAsync("actual", mappings, CancellationToken.None);

        // Assert
        _calls.Where(call => call.Arguments.Contains("--publish")).Should().HaveCount(2);
        _calls
            .Last(call => call.Arguments.Contains("--publish"))
            .Arguments.Should()
            .Equal("ports", "actual", "--publish", "127.0.0.1:28204:58204/tcp4", "--json");
        _calls.Should().NotContain(call => call.Arguments.Contains("--unpublish"));
    }

    [Fact]
    public async Task OffsetConflictFromRelayCannotTakeOverAnOtherwiseMatchingDirectNativeMapping()
    {
        // Arrange
        _inventory =
            """[{"host_ip":"127.0.0.1","host_port":28203,"sandbox_port":8203,"protocol":"tcp4"},{"host_ip":"127.0.0.1","host_port":18204,"sandbox_port":58204,"protocol":"tcp4"}]""";
        _relayFailure = new ProcessResult(
            1,
            "",
            "Requested offset change conflicts with existing host port 18204 -> sandbox target 58204. Explicitly remove the exact old binding."
        );

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 28203)], CancellationToken.None);

        // Assert
        await action
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*offset change*18204*exact old binding*");
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--publish") || call.Arguments.Contains("--unpublish")
            );
        _inventory.Should().Contain("18204").And.Contain("8203");
    }

    [Theory]
    [InlineData(0, 8203)]
    [InlineData(10000, 18203)]
    [InlineData(20000, 28203)]
    public async Task UnprovenDirectNativeBindingsAreConflictsAtEveryOffset(
        int offset,
        int hostPort
    )
    {
        // Arrange
        _inventory = $$"""
            [{"host_ip":"127.0.0.1","host_port":{{hostPort}},"sandbox_port":8203,"protocol":"tcp4"},
              {"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
            """;
        var original = _inventory;

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 8203 + offset)], CancellationToken.None);

        // Assert
        await action
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage(
                $"*conflicting runtime mapping*127.0.0.1:{hostPort}:8203/tcp4*explicitly remove*exact binding*"
            );
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--unpublish") || call.Arguments.Contains("--publish")
            );
        _inventory.Should().Be(original);
    }

    [Fact]
    public async Task AllRequestedConflictsAreCheckedBeforePublishingAnyMissingMapping()
    {
        // Arrange
        _relayInventory =
            "[{\"application_port\":8203,\"relay_port\":58203},{\"application_port\":8204,\"relay_port\":58204}]";
        _inventory = """
            [{"host_ip":"127.0.0.1","host_port":18204,"sandbox_port":8204,"protocol":"tcp4"},
             {"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
            """;

        // Act
        var action = () =>
            _client.PublishAsync(
                "actual",
                [new(8203, 18203), new(8204, 18204)],
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*18204*conflicting*");
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--unpublish") || call.Arguments.Contains("--publish")
            );
    }

    [Fact]
    public async Task PublishesDedicatedRelayRatherThanOccupyingTheApplicationListener()
    {
        // Arrange
        _acknowledgeWithoutPublishing = true;
        _inventory =
            "[{\"host_ip\":\"127.0.0.1\",\"host_port\":18203,\"sandbox_port\":58203,\"protocol\":\"tcp4\"}]";

        // Act
        await _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        _calls.Should().ContainSingle(call => call.Arguments.Contains("python3"));
        _calls.Should().NotContain(call => call.Arguments.Contains("--publish"));
        var relay = _calls.Single(call => call.Arguments.Contains("python3"));
        relay.Arguments.Take(6).Should().Equal("exec", "-i", "actual", "python3", "-I", "-c");
        relay
            .Arguments.Skip(7)
            .Should()
            .Equal(
                "8203",
                "--reserved",
                "58203",
                "--offset",
                "10000",
                "--bindings",
                "[[58203,18203]]"
            );
        relay.AttachConsole.Should().BeFalse();
        relay.UseUtf8StandardInput.Should().BeTrue();
        relay.StandardInput.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[]")]
    [InlineData("[{\"application_port\":8204,\"relay_port\":58203}]")]
    [InlineData("[{\"application_port\":8203,\"relay_port\":8203}]")]
    [InlineData("[{\"application_port\":8203,\"relay_port\":65536}]")]
    [InlineData("[{\"application_port\":8203,\"relay_port\":58203,\"relay_port\":58204}]")]
    public async Task UnconfirmedRelayListenersPreventHostPublication(string json)
    {
        // Arrange
        _relayInventory = json;

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*relay*confirm*retry*");
        _calls.Should().NotContain(call => call.Arguments.Contains("--publish"));
    }

    [Fact]
    public async Task MissingPythonIsActionableAndCannotPublishAnUnreachableNativeMapping()
    {
        // Arrange
        _relayFailure = new ProcessResult(127, "", "python3: not found");

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        await action
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*Python 3.8+*nothing is installed automatically*python3: not found*");
        _calls.Should().NotContain(call => call.Arguments.Contains("--publish"));
    }

    [Fact]
    public async Task CancellationWhileStartingRelaysDoesNotProceedToHostPublication()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("python3")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<ProcessResult>(cancellation.Token);
            });

        // Act
        var action = () => _client.PublishAsync("actual", [new(8203, 18203)], cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        _calls.Should().NotContain(call => call.Arguments.Contains("--publish"));
    }

    [Fact]
    public async Task MissingPostPublishConfirmationIsNotReportedAsSuccess()
    {
        // Arrange
        _acknowledgeWithoutPublishing = true;

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*did not confirm*retry*");
    }

    [Theory]
    [InlineData("127.0.0.1", 58204, "tcp4")]
    [InlineData("0.0.0.0", 58203, "tcp4")]
    [InlineData("127.0.0.1", 58203, "tcp")]
    public async Task ConfirmationCannotSubstituteAnotherTargetOrExposure(
        string hostIp,
        int target,
        string protocol
    )
    {
        // Arrange
        _publicationInventory = $$"""
            [{"host_ip":"{{hostIp}}","host_port":18203,"sandbox_port":{{target}},"protocol":"{{protocol}}"}]
            """;

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*did not confirm*retry*");
        _calls.Should().NotContain(call => call.Arguments.Contains("--unpublish"));
    }

    [Fact]
    public async Task PreCancelledPublishingMakesNoRuntimeCalls()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () => _client.PublishAsync("actual", [new(8203, 18203)], cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData(
        "[{\"host_ip\":\"127.0.0.1\",\"host_port\":18203,\"sandbox_port\":9000,\"protocol\":\"tcp4\"}]"
    )]
    [InlineData(
        "[{\"host_ip\":\"0.0.0.0\",\"host_port\":18203,\"sandbox_port\":8203,\"protocol\":\"tcp4\"}]"
    )]
    [InlineData(
        "[{\"host_ip\":\"127.0.0.1\",\"host_port\":18203,\"sandbox_port\":8203,\"protocol\":\"unknown\"}]"
    )]
    public async Task UnknownOrConflictingInventoryCannotTriggerPublication(string json)
    {
        // Arrange
        _inventory = json;

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--publish") || call.Arguments.Contains("--unpublish")
            );
    }

    [Theory]
    [InlineData(0, 18203)]
    [InlineData(65536, 18203)]
    [InlineData(8203, 0)]
    [InlineData(8203, 65536)]
    public async Task InvalidMappingsAreRejectedBeforeAnyRuntimeCall(int sandboxPort, int hostPort)
    {
        // Arrange
        var mappings = new ApplicationPortMapping[] { new(sandboxPort, hostPort) };

        // Act
        var action = () => _client.PublishAsync("actual", mappings, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task OccupiedHostPortsPreserveRuntimeDiagnosticsWithoutAttemptingRemoval()
    {
        // Arrange
        _publishFailure = new ProcessResult(
            1,
            "",
            "host 127.0.0.1:18203 is already in use; choose a different host port"
        );

        // Act
        var action = () =>
            _client.PublishAsync("actual", [new(8203, 18203)], CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*18203*already in use*");
        _calls.Should().NotContain(call => call.Arguments.Contains("--unpublish"));
    }

    [Fact]
    public async Task StartsBeforeInventoryAndReusesMappingsWithoutRemovingUnrelatedOnes()
    {
        // Arrange
        _inventory = """
            [{"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
            """;

        // Act
        await _client.PublishAsync(
            "actual-legacy-name",
            [new(8203, 18203), new(8203, 18203)],
            CancellationToken.None
        );
        await _client.PublishAsync(
            "actual-legacy-name",
            [new(8203, 18203)],
            CancellationToken.None
        );

        // Assert
        _calls[0].Arguments.Should().Equal("exec", "actual-legacy-name", "true");
        _calls[0].AttachConsole.Should().BeFalse();
        _calls[0].StandardInput.Should().Be("");
        _calls
            .Where(call => call.Arguments.Contains("--publish"))
            .Should()
            .ContainSingle()
            .Which.Arguments.Should()
            .Equal(
                "ports",
                "actual-legacy-name",
                "--publish",
                "127.0.0.1:18203:58203/tcp4",
                "--json"
            );
        _calls
            .Should()
            .OnlyContain(call => call.FileName == "sbx" && !call.Arguments.Contains("--unpublish"));
    }

    public void Dispose() => _root.Dispose();
}
