using Abox.Features.Shared.UserSecrets;
using Abox.Infrastructure.Dotnet;

namespace Abox.Tests;

public sealed class DotnetUserSecretStoresTests
{
    [Fact]
    public async Task AppDataTakesPrecedenceOverHome()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("appdata/Microsoft/UserSecrets/sample/secrets.json", """{"From":"appdata"}""");
        root.File("home/.microsoft/usersecrets/sample/secrets.json", """{"From":"home"}""");
        var stores = new DotnetUserSecretStores(
            Path.Combine(root.Path, "appdata"),
            Path.Combine(root.Path, "home")
        );

        // Act
        var store = await stores.ReadAsync("sample", CancellationToken.None);

        // Assert
        store.State.Should().Be(HostUserSecretStoreState.Read);
        store.Json.Should().Be("""{"From":"appdata"}""");
    }

    [Fact]
    public async Task WithoutAppDataTheStoreIsReadBelowHome()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(".microsoft/usersecrets/sample/secrets.json", """{"From":"home"}""");
        var stores = new DotnetUserSecretStores(null, root.Path);

        // Act
        var store = await stores.ReadAsync("sample", CancellationToken.None);

        // Assert
        store.Json.Should().Be("""{"From":"home"}""");
        store.ToString().Should().NotContain("home");
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    public async Task InvalidAppDataDoesNotFallBackToAnOtherwiseUsableHome(string appData)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(".microsoft/usersecrets/sample/secrets.json", "{}");
        var stores = new DotnetUserSecretStores(appData, root.Path);

        // Act
        var action = () => stores.ReadAsync("sample", CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*APPDATA*HOME*");
    }

    [Fact]
    public async Task AMissingStoreIsReportedAsMissing()
    {
        // Arrange
        using var root = new TempDirectory();
        root.CreateDirectory(".microsoft/usersecrets");
        var stores = new DotnetUserSecretStores(null, root.Path);

        // Act
        var store = await stores.ReadAsync("sample", CancellationToken.None);

        // Assert
        store.State.Should().Be(HostUserSecretStoreState.Missing);
    }

    [Fact]
    public async Task AStoreWithInvalidUtf8IsReportedAsSuch()
    {
        // Arrange
        using var root = new TempDirectory();
        var path = root.File(".microsoft/usersecrets/sample/secrets.json", "");
        File.WriteAllBytes(path, [(byte)'{', 0xFF, (byte)'}']);
        var stores = new DotnetUserSecretStores(null, root.Path);

        // Act
        var store = await stores.ReadAsync("sample", CancellationToken.None);

        // Assert
        store.State.Should().Be(HostUserSecretStoreState.InvalidUtf8);
    }

    [Fact]
    public void StoreIdsAreListedInAStableOrderAndNoneWithoutAStoreRoot()
    {
        // Arrange
        using var root = new TempDirectory();
        var stores = new DotnetUserSecretStores(null, root.Path);

        // Act
        var none = stores.StoreIds(CancellationToken.None);

        // Assert
        none.Should().BeEmpty();

        // Arrange
        root.CreateDirectory(".microsoft/usersecrets/beta");
        root.CreateDirectory(".microsoft/usersecrets/Alpha");
        root.CreateDirectory(".microsoft/usersecrets/gamma");

        // Act
        var ids = stores.StoreIds(CancellationToken.None);

        // Assert
        ids.Should().Equal("Alpha", "beta", "gamma");
    }
}
