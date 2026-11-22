using Abox.Infrastructure.Sbx;

namespace Abox.Tests;

public sealed class DevelopmentCertificateFrameTests
{
    [Theory]
    [InlineData(0, "valid")]
    [InlineData(1048577, "valid")]
    [InlineData(1, "")]
    [InlineData(1, "nul\0password")]
    [InlineData(1, null)]
    [InlineData(1, "oversized")]
    public void InvalidPayloadIsNotFramed(int length, string? password)
    {
        // Arrange
        if (password == "oversized")
            password = new string('é', 1025);

        // Act
        var frame = DevelopmentCertificateFrame.Create(new byte[length], password!, null);

        // Assert
        frame.Should().BeNull();
    }

    [Fact]
    public void MaximumPayloadAndMultibytePasswordAreAccepted()
    {
        // Arrange
        var pfx = new byte[1048576];
        var password = new string('é', 1024);

        // Act
        var frame = DevelopmentCertificateFrame.Create(pfx, password, null);

        // Assert
        frame.Should().StartWith("ABOX-CERTIFICATE-1\n");
    }
}
