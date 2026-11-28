using Abox.Cli;
using Abox.Features.Shared.Common;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Abox.Tests;

public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Core = typeof(AboxException).Assembly;
    private static readonly System.Reflection.Assembly Host = typeof(AboxRootCommand).Assembly;

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(Core, typeof(InfrastructureServiceCollection).Assembly, Host)
        .Build();

    private const string CommandSlicePattern = @"^Abox\.Features\.(?!Shared(\.|$))[^.]+(\..+)?$";

    private static readonly IObjectProvider<IType> CommandSlices = Types()
        .That()
        .ResideInNamespaceMatching(CommandSlicePattern)
        .As("command slices");

    /// <summary>
    /// The generic technical helpers, the only types the Infrastructure root may hold;
    /// anything of one tool belongs in that tool's folder.
    /// </summary>
    private static readonly string[] GenericHelpers =
    [
        "SystemConsole",
        "TerminalInput",
        "MultiSelection",
        "IProcessRunner",
        "ProcessRunner",
        "ProcessRequest",
        "ProcessStream",
        "ProcessOutputLine",
        "ProcessOutputPolicy",
        "ProcessResult",
        "ProcessOutput",
        "PrivateStagingException",
        "ProtectedTemporaryDirectory",
        "SecretRedactor",
        "YamlDotNetParser",
        "InfrastructureServiceCollection",
    ];

    [Fact]
    public void NoCommandSliceDependsOnAnotherCommandSlice()
    {
        // Arrange
        var slices = Architecture
            .Namespaces.Select(@namespace => @namespace.FullName.Split('.'))
            .Where(parts => parts is ["Abox", "Features", not "Shared", ..])
            .Select(parts => parts[2])
            .Distinct()
            .ToList();
        slices.Should().Contain(["Build", "Run", "Sandbox"]);

        // Act
        var rules = slices.Select(
            IArchRule (slice) =>
                Types()
                    .That()
                    .ResideInNamespaceMatching(InSlice(slice))
                    .Should()
                    .NotDependOnAny(
                        Types()
                            .That()
                            .ResideInNamespaceMatching(CommandSlicePattern)
                            .And()
                            .DoNotResideInNamespaceMatching(InSlice(slice))
                            .As("other command slices")
                    )
                    .Because(
                        "command slices meet only through interfaces and models in shared features"
                    )
        );

        // Assert
        rules.Aggregate((first, second) => first.And(second)).Check(Architecture);
    }

    [Fact]
    public void SharedFeaturesNeverDependOnACommandSlice()
    {
        // Arrange
        var rule = Types()
            .That()
            .ResideInNamespaceMatching(@"^Abox\.Features\.Shared(\..+)?$")
            .Should()
            .NotDependOnAny(CommandSlices);

        // Act
        var check = () => rule.Check(Architecture);

        // Assert
        check.Should().NotThrow();
    }

    [Fact]
    public void CoreDependsOnNeitherInfrastructureNorYamlDotNet()
    {
        // Arrange
        var rule = Types()
            .That()
            .ResideInAssembly(Core)
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^(Abox\.Infrastructure|YamlDotNet)(\..+)?$");

        // Act
        var check = () => rule.Check(Architecture);

        // Assert
        check.Should().NotThrow();
    }

    [Fact]
    public void TheHostHoldsOnlyTheEntryPointAndTheCommandLineRoot()
    {
        // Arrange
        var rule = Types()
            .That()
            .ResideInAssembly(Host)
            .And()
            .DoNotHaveFullName("Program")
            .And()
            .DoNotHaveFullNameMatching(@"^<")
            .Should()
            .ResideInNamespace("Abox.Cli");

        // Act
        var check = () => rule.Check(Architecture);

        // Assert
        check.Should().NotThrow();
    }

    [Fact]
    public void EverySbxAdapterCallsSbxThroughTheOneInvoker()
    {
        // Arrange
        var rule = Types()
            .That()
            .ResideInNamespace(typeof(SbxInvoker).Namespace!)
            .And()
            .DoNotHaveFullNameMatching($@"^{typeof(SbxInvoker).FullName}(\+.*)?$")
            .Should()
            .NotDependOnAny(typeof(IProcessRunner));

        // Act
        var check = () => rule.Check(Architecture);

        // Assert
        check.Should().NotThrow();
    }

    [Fact]
    public void TheInfrastructureRootHoldsOnlyTheGenericHelpers()
    {
        // Arrange
        var helpers = string.Join("|", GenericHelpers);
        var rule = Types()
            .That()
            .ResideInNamespace("Abox.Infrastructure")
            .Should()
            .HaveFullNameMatching($@"^Abox\.Infrastructure\.({helpers})(\+.*)?$");

        // Act
        var check = () => rule.Check(Architecture);

        // Assert
        check.Should().NotThrow();
    }

    private static string InSlice(string slice) => $@"^Abox\.Features\.{slice}(\..+)?$";
}
