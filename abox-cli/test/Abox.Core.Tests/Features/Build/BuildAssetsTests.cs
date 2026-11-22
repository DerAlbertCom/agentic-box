using Abox.Features.Build;

namespace Abox.Tests;

public sealed class BuildAssetsTests
{
    [Fact]
    public void TheInspectionCertificateChainIsAnEmbeddedResourceOfTheCoreAssembly()
    {
        // Arrange
        var core = typeof(WorkloadKitGenerator).Assembly;

        // Act
        using var resource = core.GetManifestResourceStream(
            "Abox.Features.Build.Assets.rwe_inspection_certificate_chain.pem"
        );

        // Assert
        core.GetName().Name.Should().Be("Abox.Core");
        resource.Should().NotBeNull();
        using var reader = new StreamReader(resource!);
        reader.ReadToEnd().Should().Contain("-----BEGIN CERTIFICATE-----");
    }
}
