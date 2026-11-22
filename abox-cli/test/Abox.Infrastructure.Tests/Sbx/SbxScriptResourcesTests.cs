using Abox.Infrastructure.Sbx;

namespace Abox.Tests;

public sealed class SbxScriptResourcesTests
{
    [Theory]
    [InlineData("Abox.ApplicationPortsRelay", "Private sbx receiver.")]
    [InlineData("Abox.DevelopmentCertificateReceiver", "Private runtime certificate transfer")]
    [InlineData("Abox.DevelopmentCertificateExec", "digest=$1; shift")]
    [InlineData("Abox.DevelopmentCertificateTrust", "Install one exact PUBLIC leaf")]
    public void TheSbxScriptsLoadFromTheInfrastructureAssemblyUnderTheirLogicalNames(
        string name,
        string excerpt
    )
    {
        // Arrange
        var infrastructure = typeof(SbxInvoker).Assembly;

        // Act
        using var resource = infrastructure.GetManifestResourceStream(name);

        // Assert
        infrastructure.GetName().Name.Should().Be("Abox.Infrastructure");
        resource.Should().NotBeNull();
        using var reader = new StreamReader(resource!);
        reader.ReadToEnd().Should().Contain(excerpt);
    }
}
