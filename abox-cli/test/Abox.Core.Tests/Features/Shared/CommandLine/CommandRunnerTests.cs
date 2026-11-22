using System.CommandLine;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Yaml;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class CommandRunnerTests
{
    private readonly TestConsole _console = new();
    private readonly EchoHandler _handler = new();
    private readonly IServiceCollection _services = new ServiceCollection();

    public CommandRunnerTests()
    {
        _services.AddSingleton<IConsole>(_console);
        _services.AddSingleton(
            new InvocationDefaults(
                new Lazy<AboxConfig>(() => new AboxConfig { DefaultTeam = "sample" }),
                Substitute.For<IYamlParser>()
            )
        );
        _services.AddSingleton<CommandRunner>();
        _services.AddSingleton<ICommandHandler<EchoParameters>>(_handler);
    }

    [Fact]
    public async Task TheHandlerGetsTheBoundParameters()
    {
        // Arrange
        using var provider = _services.BuildServiceProvider();
        var command = new EchoCommand(provider.GetRequiredService<CommandRunner>());

        // Act
        var exitCode = await command.Parse(["--value", "hello"]).InvokeAsync();

        // Assert
        exitCode.Should().Be(7);
        _handler.Received.Should().Be(new EchoParameters("hello", "sample"));
        _console.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAboxErrorFromTheBindingIsReportedWithExitCodeOne()
    {
        // Arrange
        using var provider = _services.BuildServiceProvider();
        var command = new EchoCommand(provider.GetRequiredService<CommandRunner>());

        // Act
        var exitCode = await command.Parse(["--fail-binding"]).InvokeAsync();

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
        using var provider = _services.BuildServiceProvider();
        var command = new EchoCommand(provider.GetRequiredService<CommandRunner>());

        // Act
        var exitCode = await command.Parse([]).InvokeAsync();

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
        using var provider = _services.BuildServiceProvider();
        var command = new EchoCommand(provider.GetRequiredService<CommandRunner>());

        // Act
        var exitCode = await command.Parse([]).InvokeAsync(cancellationToken: cancellation.Token);

        // Assert
        exitCode.Should().Be(130);
        _console.Errors.Should().Equal("error: the command was cancelled.");
    }

    [Fact]
    public async Task TheHandlerIsResolvedOnlyWhenTheActionRuns()
    {
        // Arrange
        var resolutions = 0;
        _services.AddTransient<ICommandHandler<EchoParameters>>(_ =>
        {
            resolutions++;
            return _handler;
        });
        using var provider = _services.BuildServiceProvider();
        var command = new EchoCommand(provider.GetRequiredService<CommandRunner>());
        var parsed = command.Parse([]);
        var resolutionsBeforeTheAction = resolutions;

        // Act
        await parsed.InvokeAsync();

        // Assert
        resolutionsBeforeTheAction.Should().Be(0);
        resolutions.Should().Be(1);
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
