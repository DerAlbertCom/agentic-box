using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.UserSecrets;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsFilteringTests
{
    [Fact]
    public async Task TheSharedFlowKeepsOnlySelectedOriginalValuesInItsOpaqueSnapshot()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.yaml", """{"userSecrets":[{"id":"sample","keys":["Nested:Keep"]}]}""");
        var host = Substitute.For<IHostUserSecretStores>();
        host.ReadAsync("sample", Arg.Any<CancellationToken>())
            .Returns(
                HostUserSecretStore.Read(
                    """
                    {"Nested":{"Keep":"synthetic-original","Ignored":"https://PROD.EXAMPLE/unmatched"}}
                    """
                )
            );
        var sandbox = Substitute.For<ISandboxUserSecrets>();
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = ["prod.example"] },
            host,
            sandbox
        );

        // Act
        var prepared = await copy.PrepareAsync(
            await FolderConfig.LoadAsync(root.Path, new YamlDotNetParser(), CancellationToken.None),
            false,
            CancellationToken.None
        );
        // Assert
        sandbox.ReceivedCalls().Should().BeEmpty();

        // Arrange
        host.ReadAsync("sample", Arg.Any<CancellationToken>())
            .Returns(HostUserSecretStore.Read("malformed changed after validation"));

        // Act
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        // Assert
        await sandbox
            .Received(1)
            .WriteUserSecretsAsync(
                "sandbox",
                "sample",
                """{"Nested:Keep":"synthetic-original"}""",
                Arg.Any<CancellationToken>()
            );
        report.Stores.Should().ContainSingle().Which.KeyCount.Should().Be(1);
        report.ToString().Should().NotContain("synthetic-");
        prepared.ToString().Should().NotContain("synthetic-").And.NotContain("PROD.EXAMPLE");
    }
}
