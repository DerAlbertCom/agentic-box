using Abox.Features.Build;

namespace Abox.Tests;

public sealed class BuildAssetsTests
{
    [Fact]
    public void TheCorporateCasAreEmbeddedResourcesOfTheCoreAssembly()
    {
        // Arrange
        var core = typeof(WorkloadKitGenerator).Assembly;

        // Act
        var certificates = WorkloadKitGenerator.BundledCertificates();

        // Assert
        core.GetName().Name.Should().Be("Abox.Core");
        certificates.Should().NotBeEmpty();
        foreach (var (fileName, resourceName) in certificates)
        {
            fileName.Should().EndWith(".crt");
            using var resource = core.GetManifestResourceStream(resourceName);
            using var reader = new StreamReader(resource!);
            reader.ReadToEnd().Should().StartWith("-----BEGIN CERTIFICATE-----");
        }
    }
}
