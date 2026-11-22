using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsCopyTests
{
    private readonly IHostUserSecretStores host = Substitute.For<IHostUserSecretStores>();
    private readonly ISandboxUserSecrets sandbox = Substitute.For<ISandboxUserSecrets>();

    [Theory]
    [InlineData("{\"userSecrets\":[\"sample\"]}")]
    [InlineData(
        "{\"squad\":\"AC\",\"portOffset\":20000,\"userSecrets\":[{\"id\":\"sample\",\"keys\":[\"Nested:*\"]}]}"
    )]
    public async Task PreparationIsSideEffectFreeAndCopyUsesTheOriginalInMemorySnapshot(
        string configuration
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", configuration);
        Store("sample", """{"Nested":{"Password":"synthetic-original"}}""");
        var copy = new UserSecretsCopy(new AboxConfig(), host, sandbox);

        // Act
        var prepared = await copy.PrepareAsync(
            await FolderConfig.LoadAsync(root.Path, new YamlDotNetParser(), CancellationToken.None),
            false,
            CancellationToken.None
        );
        // Assert
        sandbox.ReceivedCalls().Should().BeEmpty();

        // Arrange
        Store("sample", "malformed changed after validation");

        // Act
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        // Assert
        await sandbox
            .Received(1)
            .WriteUserSecretsAsync(
                "sandbox",
                "sample",
                Arg.Is<string>(json => json.Contains("synthetic-original")),
                Arg.Any<CancellationToken>()
            );
        report.Stores.Should().ContainSingle().Which.KeyCount.Should().Be(1);
        prepared.ToString().Should().NotContain("synthetic-original");
        report.ToString().Should().NotContain("synthetic-original");
    }

    [Fact]
    public async Task MissingFolderConfigurationIsANoopWithoutTouchingHostOrSandbox()
    {
        // Arrange
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = [""] },
            host,
            sandbox
        );

        // Act
        var prepared = await copy.PrepareAsync(null, false, CancellationToken.None);
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        // Assert
        report.Stores.Should().BeEmpty();
        host.ReceivedCalls().Should().BeEmpty();
        sandbox.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectlyConstructedNullDenyPolicyIsRejectedEvenWithForce(bool force)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", """{"userSecrets":["missing"]}""");
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = null! },
            host,
            sandbox
        );
        var folder = await FolderConfig.LoadAsync(
            root.Path,
            new YamlDotNetParser(),
            CancellationToken.None
        );

        // Act
        var action = () => copy.PrepareAsync(folder, force, CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>().WithMessage("*userSecretsDenyList*");
        host.ReceivedCalls().Should().BeEmpty();
        sandbox.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(HostUserSecretStore.Missing))]
    [InlineData(nameof(HostUserSecretStore.Unreadable))]
    public async Task AStoreThatCannotBeReadWithoutProjectSettingsStopsTheCopy(string state)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", """{"userSecrets":["sample"]}""");
        host.ReadAsync("sample", Arg.Any<CancellationToken>())
            .Returns(
                state == nameof(HostUserSecretStore.Missing)
                    ? HostUserSecretStore.Missing
                    : HostUserSecretStore.Unreadable
            );
        var copy = new UserSecretsCopy(new AboxConfig(), host, sandbox);
        var folder = await FolderConfig.LoadAsync(
            root.Path,
            new YamlDotNetParser(),
            CancellationToken.None
        );

        // Act
        var action = () => copy.PrepareAsync(folder, false, CancellationToken.None);

        // Assert
        await action
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("Cannot read user-secret store 'sample'.*appsettings.json*");
        sandbox.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task AStoreWithInvalidUtf8StopsTheCopy()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", """{"userSecrets":["sample"]}""");
        host.ReadAsync("sample", Arg.Any<CancellationToken>())
            .Returns(HostUserSecretStore.InvalidUtf8);
        var copy = new UserSecretsCopy(new AboxConfig(), host, sandbox);
        var folder = await FolderConfig.LoadAsync(
            root.Path,
            new YamlDotNetParser(),
            CancellationToken.None
        );

        // Act
        var action = () => copy.PrepareAsync(folder, false, CancellationToken.None);

        // Assert
        await action
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("User-secret store 'sample' has invalid UTF-8.*");
        sandbox.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationIsPreservedByBothPhases()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", """{"userSecrets":["sample"]}""");
        Store("sample", "{}");
        var copy = new UserSecretsCopy(new AboxConfig(), host, sandbox);
        var folder = await FolderConfig.LoadAsync(
            root.Path,
            new YamlDotNetParser(),
            CancellationToken.None
        );
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act
        var preparation = () => copy.PrepareAsync(folder, false, cancelled.Token);

        // Assert
        await preparation.Should().ThrowAsync<OperationCanceledException>();

        // Arrange
        var prepared = await copy.PrepareAsync(folder, false, CancellationToken.None);

        // Act
        var copying = () => copy.CopyAsync(prepared, "sandbox", cancelled.Token);

        // Assert
        await copying.Should().ThrowAsync<OperationCanceledException>();
        await sandbox
            .DidNotReceive()
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    private void Store(string id, string json) =>
        host.ReadAsync(id, Arg.Any<CancellationToken>()).Returns(HostUserSecretStore.Read(json));
}
