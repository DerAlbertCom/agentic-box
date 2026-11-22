using System.CommandLine;
using System.Reflection;
using Abox.Features.Build;
using Abox.Features.Run;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Launchers;

namespace Abox.Tests;

/// <summary>
/// The architecture rule for commands that ArchUnitNET cannot express: a command describes
/// only its syntax, so its constructor takes only the command runner.
/// </summary>
public sealed class CommandConstructorTests
{
    /// <summary>
    /// The services a command constructor may take besides the command runner. Every entry is
    /// a reviewed decision: <c>run</c> lists the launchers in its help and completions.
    /// </summary>
    private static readonly Dictionary<Type, Type[]> AllowedDependencies = new()
    {
        [typeof(RunCommand)] = [typeof(LauncherRegistry)],
    };

    [Fact]
    public void EveryCommandConstructorTakesOnlyTheCommandRunner()
    {
        // Arrange
        var commands = ConcreteCommands(typeof(AboxException).Assembly.GetTypes());
        commands.Should().Contain([typeof(BuildCommand), typeof(RunCommand)]);

        // Act
        var violations = Violations(commands);

        // Assert
        violations.Should().BeEmpty();
    }

    [Fact]
    public void NoCommandConstructorTakesALazyService()
    {
        // Arrange
        var commands = ConcreteCommands(typeof(AboxException).Assembly.GetTypes());

        // Act
        var lazyDependencies = LazyDependencies(commands);

        // Assert
        lazyDependencies.Should().BeEmpty();
    }

    [Fact]
    public void ACommandThatTakesAnotherServiceIsReported()
    {
        // Arrange
        var commands = ConcreteCommands([typeof(CommandWithService), typeof(RunnerOnlyCommand)]);

        // Act
        var violations = Violations(commands);

        // Assert
        violations
            .Should()
            .Equal($"{typeof(CommandWithService).FullName} takes {typeof(IConsole).FullName}");
    }

    [Fact]
    public void ACommandThatTakesALazyServiceIsReported()
    {
        // Arrange
        var commands = ConcreteCommands([typeof(CommandWithLazyRunner)]);
        var expected =
            $"{typeof(CommandWithLazyRunner).FullName} takes {typeof(Lazy<CommandRunner>).FullName}";

        // Act
        var violations = Violations(commands);
        var lazyDependencies = LazyDependencies(commands);

        // Assert
        violations.Should().Equal(expected);
        lazyDependencies.Should().Equal(expected);
    }

    private static List<Type> ConcreteCommands(IEnumerable<Type> types) =>
        types.Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Command))).ToList();

    private static List<string> Violations(IEnumerable<Type> commands) =>
        Dependencies(commands)
            .Where(dependency =>
                dependency.Parameter != typeof(CommandRunner)
                && !(
                    AllowedDependencies.TryGetValue(dependency.Command, out var allowed)
                    && allowed.Contains(dependency.Parameter)
                )
            )
            .Select(Describe)
            .ToList();

    private static List<string> LazyDependencies(IEnumerable<Type> commands) =>
        Dependencies(commands)
            .Where(dependency =>
                dependency.Parameter.IsGenericType
                && dependency.Parameter.GetGenericTypeDefinition() == typeof(Lazy<>)
            )
            .Select(Describe)
            .ToList();

    private static IEnumerable<(Type Command, Type Parameter)> Dependencies(
        IEnumerable<Type> commands
    ) =>
        commands.SelectMany(command =>
            command
                .GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                )
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => (command, parameter.ParameterType))
        );

    private static string Describe((Type Command, Type Parameter) dependency) =>
        $"{dependency.Command.FullName} takes {dependency.Parameter.FullName}";

    private sealed class RunnerOnlyCommand(CommandRunner runner)
        : AboxCommand<string>("runner-only", "Takes only the command runner.", runner)
    {
        internal override Task<string> BindAsync(
            ParseResult parseResult,
            InvocationDefaults defaults,
            CancellationToken cancellationToken
        ) => Task.FromResult("");
    }

    private sealed class CommandWithService(IConsole console) : Command("with-service")
    {
        public IConsole Console { get; } = console;
    }

    private sealed class CommandWithLazyRunner(Lazy<CommandRunner> runner) : Command("with-lazy")
    {
        public Lazy<CommandRunner> Runner { get; } = runner;
    }
}
