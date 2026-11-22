using System.Reflection;
using System.Reflection.Emit;
using Abox.Cli;

namespace Abox.Tests;

public sealed class ArchitectureTests
{
    private const string Features = "Abox.Features";
    private const string SharedFeatures = "Abox.Features.Shared";
    private const string Infrastructure = "Abox.Infrastructure";
    private const string CompositionRoot = "Abox.Cli";

    [Fact]
    public void SharedFeaturesAndInfrastructureDoNotReferenceACommandSlice()
    {
        // Arrange
        var references = ProjectReferences();

        // Act
        var violations = references
            .Where(reference =>
                IsIn(reference.From, SharedFeatures) || IsIn(reference.From, Infrastructure)
            )
            .Where(reference => IsCommandSlice(reference.To))
            .Select(reference => reference.ToString());

        // Assert
        violations.Should().BeEmpty();
    }

    [Fact]
    public void FeaturesDoNotReferenceTheCompositionRoot()
    {
        // Arrange
        var references = ProjectReferences();

        // Act
        var violations = references
            .Where(reference => IsIn(reference.From, Features))
            .Where(reference => IsIn(reference.To, CompositionRoot))
            .Select(reference => reference.ToString());

        // Assert
        violations.Should().BeEmpty();
    }

    [Fact]
    public void FeaturesDoNotReferenceAnExternalToolAdapter()
    {
        // Arrange
        var references = ProjectReferences();

        // Act
        var violations = references
            .Where(reference => IsIn(reference.From, Features))
            .Where(reference => IsToolAdapter(reference.To))
            .Select(reference => reference.ToString());

        // Assert
        violations.Should().BeEmpty();
    }

    [Fact]
    public void OnlyTheCompositionRootReferencesAnExternalToolAdapterFromOutsideItsTool()
    {
        // Arrange
        var references = ProjectReferences();

        // Act
        var violations = references
            .Where(reference => IsToolAdapter(reference.To))
            .Where(reference => !IsIn(reference.From, CompositionRoot))
            .Where(reference => !IsIn(reference.From, reference.To))
            .Select(reference => reference.ToString());

        // Assert
        violations.Should().BeEmpty();
    }

    private static bool IsIn(string @namespace, string module) =>
        @namespace == module || @namespace.StartsWith(module + ".", StringComparison.Ordinal);

    private static bool IsCommandSlice(string @namespace) =>
        IsIn(@namespace, Features) && @namespace != Features && !IsIn(@namespace, SharedFeatures);

    private static bool IsToolAdapter(string @namespace) =>
        IsIn(@namespace, Infrastructure) && @namespace != Infrastructure;

    private sealed record Reference(string Type, string From, string To)
    {
        public override string ToString() => $"{Type} -> {To}";
    }

    /// <summary>
    /// Every namespace of the abox assembly that each top-level type uses: in its
    /// signatures, its local variables and the instructions of its method bodies,
    /// including the compiler-generated types nested in it.
    /// </summary>
    private static IReadOnlyList<Reference> ProjectReferences()
    {
        var assembly = typeof(AboxRootCommand).Assembly;
        var references = new HashSet<Reference>();
        foreach (var type in assembly.GetTypes())
        {
            var owner = type;
            while (owner.DeclaringType is not null)
            {
                owner = owner.DeclaringType;
            }

            if (owner.Namespace is null)
            {
                continue;
            }

            foreach (var used in UsedTypes(type))
            {
                foreach (var @namespace in Namespaces(used, assembly))
                {
                    if (@namespace != owner.Namespace)
                    {
                        references.Add(new Reference(owner.FullName!, owner.Namespace, @namespace));
                    }
                }
            }
        }

        return references.OrderBy(reference => reference.ToString()).ToList();
    }

    private static IEnumerable<string> Namespaces(Type type, Assembly assembly)
    {
        if (type.IsGenericParameter)
        {
            yield break;
        }

        if (type.HasElementType)
        {
            foreach (var @namespace in Namespaces(type.GetElementType()!, assembly))
            {
                yield return @namespace;
            }

            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var @namespace in Namespaces(argument, assembly))
                {
                    yield return @namespace;
                }
            }
        }

        if (type.Assembly == assembly && type.Namespace is not null)
        {
            yield return type.Namespace;
        }
    }

    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly
        | BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static;

    private static IEnumerable<Type> UsedTypes(Type type)
    {
        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (var contract in type.GetInterfaces())
        {
            yield return contract;
        }

        foreach (var field in type.GetFields(Declared))
        {
            yield return field.FieldType;
        }

        foreach (var property in type.GetProperties(Declared))
        {
            yield return property.PropertyType;
        }

        var methods = type.GetMethods(Declared)
            .Cast<MethodBase>()
            .Concat(type.GetConstructors(Declared));
        foreach (var method in methods)
        {
            if (method is MethodInfo { ReturnType: var returnType })
            {
                yield return returnType;
            }

            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var used in BodyTypes(type, method))
            {
                yield return used;
            }
        }
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    private static IEnumerable<Type> BodyTypes(Type type, MethodBase method)
    {
        var body = method.GetMethodBody();
        if (body is null)
        {
            yield break;
        }

        foreach (var local in body.LocalVariables)
        {
            yield return local.LocalType;
        }

        var il = body.GetILAsByteArray() ?? [];
        var typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;
        while (position < il.Length)
        {
            var value =
                il[position++] == 0xFE
                    ? unchecked((short)(0xFE00 | il[position++]))
                    : il[position - 1];
            var code = OpCodesByValue[value];
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    position += 1;
                    break;
                case OperandType.InlineVar:
                    position += 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    position += 8;
                    break;
                case OperandType.InlineSwitch:
                    position += 4 + 4 * BitConverter.ToInt32(il, position);
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    var member = method.Module.ResolveMember(
                        BitConverter.ToInt32(il, position),
                        typeArguments,
                        methodArguments
                    );
                    position += 4;
                    foreach (var used in MemberTypes(member))
                    {
                        yield return used;
                    }

                    break;
                default:
                    position += 4;
                    break;
            }
        }
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo? member)
    {
        switch (member)
        {
            case Type used:
                yield return used;
                break;
            case FieldInfo field:
                yield return field.DeclaringType!;
                yield return field.FieldType;
                break;
            case MethodBase method:
                if (method.DeclaringType is not null)
                {
                    yield return method.DeclaringType;
                }

                if (method is MethodInfo info)
                {
                    yield return info.ReturnType;
                    foreach (var argument in info.IsGenericMethod ? info.GetGenericArguments() : [])
                    {
                        yield return argument;
                    }
                }

                foreach (var parameter in method.GetParameters())
                {
                    yield return parameter.ParameterType;
                }

                break;
        }
    }
}
