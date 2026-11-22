using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;

namespace Abox.Tests;

public sealed class FolderConfigTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("absent")]
    public async Task MissingFileOrDirectoryIsAnAbsentSnapshot(string relativeLocation)
    {
        // Arrange
        using var root = new TempDirectory();
        var location = Path.Combine(root.Path, relativeLocation);

        // Act
        var snapshot = await FolderConfig.LoadAsync(location, CancellationToken.None);

        // Assert
        snapshot.Should().BeNull();
    }

    [Theory]
    [InlineData("abox.yml")]
    [InlineData("abox.user.yml")]
    public async Task YmlFilesAreNotRecognised(string file)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(file, "portOffset: 20000\n");

        // Act
        var snapshot = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        snapshot.Should().BeNull();
    }

    [Theory]
    [InlineData("abox.yaml")]
    [InlineData("abox.user.yaml")]
    public async Task EitherFileAloneIsTheFolderConfiguration(string file)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(
            file,
            """
            squad: ac
            portOffset: 20000
            userSecrets:
              - whole
              - id: filtered
                keys: ["Db:*"]
            """
        );

        // Act
        var snapshot = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        snapshot!.Squad.Should().Be("ac");
        FolderConfig.SelectPortOffset(snapshot).Should().Be(20000);
        snapshot.UserSecrets.Select(selection => selection.Id).Should().Equal("whole", "filtered");
    }

    [Fact]
    public async Task TheUserFileReplacesValuesAndListsAndKeepsKeysItDoesNotSet()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(
            "abox.yaml",
            """
            squad: ac
            portOffset: 20000
            userSecrets: [shared, other]
            """
        );
        root.File(
            "abox.user.yaml",
            """
            portOffset: 30000
            userSecrets: [mine]
            """
        );

        // Act
        var snapshot = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        snapshot!.Squad.Should().Be("ac");
        FolderConfig.SelectPortOffset(snapshot).Should().Be(30000);
        snapshot.UserSecrets.Select(selection => selection.Id).Should().Equal("mine");
    }

    [Fact]
    public async Task TheMergedResultIsValidatedSoTheUserFileCanReplaceAnInvalidValue()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", "squad:\nportOffset: 70000\nuserSecrets: [../escape]\n");
        root.File("abox.user.yaml", "squad: ac\nportOffset: 20000\nuserSecrets: [mine]\n");

        // Act
        var snapshot = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        snapshot!.Squad.Should().Be("ac");
        FolderConfig.SelectPortOffset(snapshot).Should().Be(20000);
        snapshot.UserSecrets.Select(selection => selection.Id).Should().Equal("mine");
    }

    [Fact]
    public async Task AnEmptyListInTheUserFileSelectsNoUserSecrets()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", "userSecrets: [shared]\n");
        root.File("abox.user.yaml", "userSecrets: []\n");

        // Act
        var snapshot = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        snapshot!.UserSecrets.Should().BeEmpty();
    }

    [Theory]
    [InlineData("abox.json", "abox.yaml")]
    [InlineData("abox.user.json", "abox.user.yaml")]
    public async Task AJsonFolderConfigurationFailsWithARenameHint(string json, string yaml)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", "portOffset: 20000\n");
        root.File(json, """{"portOffset":30000}""");

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        var failure = await load.Should().ThrowAsync<AboxException>();
        failure.Which.Message.Should().Contain(json).And.Contain($"Rename it to {yaml}");
    }

    [Fact]
    public async Task CancellationIsNotTranslatedIntoAnInvalidConfigurationError()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", "{}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, cancellation.Token);

        // Assert
        await load.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("abox.yaml")]
    [InlineData("abox.user.yaml")]
    public async Task AnUnreadableConfigurationIsNotTreatedAsAbsent(string file)
    {
        // Arrange
        using var root = new TempDirectory();
        var path = root.File(file, "synthetic-private");
        using var locked = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        var failure = await load.Should().ThrowAsync<AboxException>();
        failure
            .Which.Message.Should()
            .Be($"Cannot read '{path}'. Check the folder and file permissions.");
    }

    [Theory]
    [InlineData("abox.yaml", "[]", "must contain a mapping")]
    [InlineData("abox.user.yaml", "[]", "must contain a mapping")]
    [InlineData("abox.yaml", "squad: ac\nsquad: bd\n", "not valid YAML")]
    [InlineData("abox.user.yaml", "squad: ac\nsquad: bd\n", "not valid YAML")]
    [InlineData("abox.yaml", "Squad: synthetic-private\n", "not a known key")]
    [InlineData("abox.user.yaml", "Squad: synthetic-private\n", "not a known key")]
    [InlineData("abox.yaml", "squad:\n", "nonempty string")]
    [InlineData("abox.user.yaml", "squad: null\n", "nonempty string")]
    [InlineData("abox.yaml", "squad: '  '\n", "nonempty string")]
    [InlineData("abox.user.yaml", "squad: [ac]\n", "must be a value")]
    [InlineData("abox.yaml", "portOffset: '10000'\n", "integer")]
    [InlineData("abox.user.yaml", "portOffset: 65535\n", "integer")]
    [InlineData(
        "abox.yaml",
        "userSecrets:\n  - id: safe\n    Id: synthetic-private\n",
        "not a known key"
    )]
    [InlineData("abox.user.yaml", "userSecrets:\n  - id: safe\n    id: other\n", "not valid YAML")]
    [InlineData("abox.yaml", "userSecrets:\n  - keys: []\n", "requires a string 'id'")]
    [InlineData("abox.user.yaml", "userSecrets: [12, [x]]\n", "must be a value or a mapping")]
    [InlineData("abox.yaml", "squad: synthetic-private\n broken: [\n", "not valid YAML")]
    public async Task StrictDecodingNamesTheOffendingFileWithoutEchoingInput(
        string file,
        string yaml,
        string diagnostic
    )
    {
        // Arrange
        using var root = new TempDirectory();
        var other = file == "abox.yaml" ? "abox.user.yaml" : "abox.yaml";
        root.File(other, "terminalTheme: true\n");
        var path = root.File(file, yaml);

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        var failure = await load.Should().ThrowAsync<AboxException>();
        failure
            .Which.Message.Should()
            .Contain(path)
            .And.Contain(diagnostic)
            .And.NotContain("synthetic-private");
    }
}
