using Abox.Features.Shared.Common;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class YamlDotNetParserTests
{
    private readonly YamlDotNetParser _parser = new();

    [Fact]
    public void ParseTurnsADocumentIntoANodeTreeThatNamesItsSource()
    {
        // Arrange
        const string yaml = """
            name: value
            list: [plain, 'quoted']
            section: {key: nested}
            """;

        // Act
        var root = _parser.Parse(yaml, "abox.yaml");

        // Assert
        var mapping = root.Should().BeOfType<YamlMapping>().Subject;
        mapping.Source.Should().Be("abox.yaml");
        mapping
            .Entries.Select(entry => ((YamlValue)entry.Key).Value)
            .Should()
            .Equal("name", "list", "section");
        mapping.Entries[0].Value.Should().BeEquivalentTo(new { Value = "value", IsPlain = true });
        var list = mapping.Entries[1].Value.Should().BeOfType<YamlSequence>().Subject;
        list.Items.Should()
            .BeEquivalentTo(
                [
                    new { Value = "plain", IsPlain = true },
                    new { Value = "quoted", IsPlain = false },
                ],
                options => options.ExcludingMissingMembers()
            );
        var section = mapping.Entries[2].Value.Should().BeOfType<YamlMapping>().Subject;
        ((YamlValue)section.Entries.Single().Value).Value.Should().Be("nested");
        section
            .Entries.SelectMany(entry => new[] { entry.Key, entry.Value })
            .Concat(list.Items)
            .Should()
            .OnlyContain(node => node.Source == "abox.yaml");
    }

    [Theory]
    [InlineData("")]
    [InlineData("# only a comment\n")]
    public void ParseTurnsAnEmptyDocumentIntoAnEmptyPlainValue(string yaml)
    {
        // Act
        var root = _parser.Parse(yaml, "abox.user.yaml");

        // Assert
        root.Should()
            .BeOfType<YamlValue>()
            .Which.Should()
            .BeEquivalentTo(new { Value = "", Source = "abox.user.yaml" });
    }

    [Fact]
    public void ParseReportsInvalidYamlWithItsSource()
    {
        // Act
        var act = () => _parser.Parse("list: [one", "abox.yaml");

        // Assert
        act.Should().Throw<AboxException>().WithMessage("'abox.yaml' is not valid YAML: *");
    }

    [Fact]
    public void LoadFileReadsTheFileWithItsPathAsSource()
    {
        // Arrange
        using var root = new TempDirectory();
        var path = Path.Combine(root.Path, "squad.abox");
        File.WriteAllText(path, "base: parent");

        // Act
        var node = _parser.LoadFile(path);

        // Assert
        var entry = node.Should().BeOfType<YamlMapping>().Which.Entries.Single();
        entry.Value.Should().BeEquivalentTo(new { Value = "parent", Source = path });
    }

    [Fact]
    public void LoadFileReportsAMissingFile()
    {
        // Arrange
        using var root = new TempDirectory();
        var path = Path.Combine(root.Path, "missing.workload");

        // Act
        var act = () => _parser.LoadFile(path);

        // Assert
        act.Should().Throw<AboxException>().WithMessage($"'{path}' does not exist.");
    }
}
