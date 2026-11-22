using Abox.Features.Shared.Common;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class YamlReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarListErrorsRetainTheRootOrNestedKeyPath(bool nested)
    {
        // Arrange
        var reader = new YamlDotNetParser().Read(
            nested ? "section:\n  values: [one, {}]" : "values: [one, {}]",
            "source"
        );
        var key = nested ? "section.values" : "values";

        // Act
        var act = () =>
            nested
                ? reader.GetEntry("section")!.GetStringList("values")
                : reader.GetStringList("values");

        // Assert
        act.Should().Throw<AboxException>().WithMessage($"'source': '{key}' must be a value.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarMapErrorsRetainTheRootOrNestedKeyPath(bool nested)
    {
        // Arrange
        var reader = new YamlDotNetParser().Read(
            nested ? "section:\n  values: {item: []}" : "values: {item: []}",
            "source"
        );
        var key = nested ? "section.values.item" : "values.item";

        // Act
        var act = () =>
            nested
                ? reader.GetEntry("section")!.GetStringMap("values")
                : reader.GetStringMap("values");

        // Assert
        act.Should().Throw<AboxException>().WithMessage($"'source': '{key}' must be a value.");
    }

    [Fact]
    public void RootEntriesStillAcceptScalarsButNestedEntriesRequireMappings()
    {
        // Arrange
        var reader = new YamlDotNetParser().Read(
            "scalar: value\nsection: {scalar: value}",
            "source"
        );

        // Act
        var scalar = reader.GetEntry("scalar")!.ScalarValue;
        var act = () => reader.GetEntry("section")!.GetEntry("scalar");

        // Assert
        scalar.Should().Be("value");
        act.Should()
            .Throw<AboxException>()
            .WithMessage("'source': 'section.scalar' must be a mapping.");
    }

    [Fact]
    public void MergeMergesMappingsRecursivelyAndReplacesValuesAndLists()
    {
        // Arrange
        var shared = new YamlDotNetParser().Read(
            """
            name: shared
            list: [one, two]
            kept: value
            section: {a: shared, b: shared, inner: {x: shared, y: shared}}
            """,
            "shared"
        );
        var user = new YamlDotNetParser().Read(
            """
            name: user
            list: [three]
            section: {b: user, inner: {y: user}}
            """,
            "user"
        );

        // Act
        var merged = YamlReader.Merge(shared, user);

        // Assert
        merged.GetString("name").Should().Be("user");
        merged.GetStringList("list").Should().Equal("three");
        merged.GetString("kept").Should().Be("value");
        var section = merged.GetEntry("section")!;
        section.GetString("a").Should().Be("shared");
        section.GetString("b").Should().Be("user");
        section
            .GetStringMap("inner")
            .Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["x"] = "shared", ["y"] = "user" });
    }

    [Theory]
    [InlineData("list", "shared")]
    [InlineData("name", "user")]
    [InlineData("section.a", "shared")]
    [InlineData("section.b", "user")]
    public void MergedValueErrorsNameTheFileThatSuppliedTheValue(string key, string source)
    {
        // Arrange
        var shared = new YamlDotNetParser().Read("list: [{}]\nsection: {a: [x], b: x}", "shared");
        var user = new YamlDotNetParser().Read("name: [x]\nsection: {b: [x]}", "user");
        var merged = YamlReader.Merge(shared, user);

        // Act
        Action act = key switch
        {
            "list" => () => merged.GetStringList("list"),
            "name" => () => merged.GetString("name"),
            _ => () => merged.GetEntry("section")!.GetString(key["section.".Length..]),
        };

        // Assert
        act.Should().Throw<AboxException>().WithMessage($"'{source}': '{key}' must be a value.");
    }
}
