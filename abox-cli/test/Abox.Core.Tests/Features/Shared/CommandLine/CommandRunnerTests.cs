using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using NSubstitute;

namespace Abox.Tests;

public sealed class CommandRunnerTests
{
    private readonly TestConsole _console = new();
    private readonly EchoHandler _handler = new();
    private readonly IServiceProvider _services = Substitute.For<IServiceProvider>();
    private readonly EchoCommand _command;

    public CommandRunnerTests()
    {
        _services
            .GetService(typeof(InvocationDefaults))
            .Returns(
                new InvocationDefaults(
                    new Lazy<AboxConfig>(() => new AboxConfig { DefaultTeam = "sample" }),
                    Substitute.For<IYamlParser>(),
                    new TrailingArguments(),
                    new Lazy<SourceRootResolver>(() =>
                        throw new InvalidOperationException("No source root is used.")
                    )
                )
            );
        _services.GetService(typeof(ICommandHandler<EchoParameters>)).Returns(_handler);
        _command = new EchoCommand(new CommandRunner(_services, _console));
    }

    [Fact]
    public async Task TheHandlerGetsTheBoundParameters()
    {
        // Arrange
        var parsed = _command.Parse(["--value", "hello"]);

        // Act
        var exitCode = await parsed.InvokeAsync();

        // Assert
        exitCode.Should().Be(7);
        _handler.Received.Should().Be(new EchoParameters("hello", "sample"));
        _console.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAboxErrorFromTheBindingIsReportedWithExitCodeOne()
    {
        // Arrange
        var parsed = _command.Parse(["--fail-binding"]);

        // Act
        var exitCode = await parsed.InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        _console.Errors.Should().Equal("error: the binding failed.");
        _handler.Received.Should().BeNull();
    }

    [Fact]
    public async Task AnAboxErrorFromTheHandlerIsReportedWithExitCodeOne()
    {
        // Arrange
        _handler.Work = (_, _) => throw new AboxException("the handler failed.");

        // Act
        var exitCode = await _command.Parse([]).InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        _console.Errors.Should().Equal("error: the handler failed.");
    }

    [Fact]
    public async Task CancellationUsesTheConventionalExitCode()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        _handler.Work = async (_, cancellationToken) =>
        {
            await cancellation.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        };

        // Act
        var exitCode = await _command.Parse([]).InvokeAsync(cancellationToken: cancellation.Token);

        // Assert
        exitCode.Should().Be(130);
        _console.Errors.Should().Equal("error: the command was cancelled.");
    }

    [Fact]
    public async Task TheHandlerIsResolvedOnlyWhenTheActionRuns()
    {
        // Arrange
        var parsed = _command.Parse([]);
        var resolutionsBeforeTheAction = _services
            .ReceivedCalls()
            .Count(call => Equals(call.GetArguments()[0], typeof(ICommandHandler<EchoParameters>)));

        // Act
        await parsed.InvokeAsync();

        // Assert
        resolutionsBeforeTheAction.Should().Be(0);
        _services.Received(1).GetService(typeof(ICommandHandler<EchoParameters>));
    }

    private sealed record EchoParameters(string? Value, string Team);

    private sealed class EchoCommand : AboxCommand<EchoParameters>
    {
        private readonly Option<string> _value = new("--value");
        private readonly Option<bool> _failBinding = new("--fail-binding");

        public EchoCommand(CommandRunner runner)
            : base("echo", "Echoes its parameters.", runner)
        {
            Options.Add(_value);
            Options.Add(_failBinding);
        }

        internal override Task<EchoParameters> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        )
        {
            if (parseResult.GetValue(_failBinding))
                throw new AboxException("the binding failed.");
            return Task.FromResult(
                new EchoParameters(parseResult.GetValue(_value), defaults.Team(null))
            );
        }
    }

    private sealed class EchoHandler : ICommandHandler<EchoParameters>
    {
        public EchoParameters? Received { get; private set; }

        public Func<EchoParameters, CancellationToken, Task<int>> Work { get; set; } =
            (_, _) => Task.FromResult(7);

        public Task<int> HandleAsync(EchoParameters parameters, CancellationToken cancellationToken)
        {
            Received = parameters;
            return Work(parameters, cancellationToken);
        }
    }
}
