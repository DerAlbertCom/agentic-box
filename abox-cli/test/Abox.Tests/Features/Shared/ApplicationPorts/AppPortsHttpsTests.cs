using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Https;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Abox.Tests;

public sealed class AppPortsHttpsTests
{
    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />", true)]
    [InlineData("<Project><Sdk Name=\"Microsoft.NET.Sdk.Web\" /></Project>", true)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\" />", false)]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>",
        true
    )]
    public async Task OnlyExplicitApplicableProjectsWithLaunchPortsTriggerCertificateSetup(
        string project,
        bool applicable
    )
    {
        await AssertCertificateSetupAsync(project, launchPorts: true, expected: applicable);
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk.Web\" />")]
    [InlineData(
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><FrameworkReference Include=\"Microsoft.AspNetCore.App\" /></ItemGroup></Project>"
    )]
    public async Task ProjectsWithoutLaunchPortsTriggerNoCertificateSetup(string project)
    {
        await AssertCertificateSetupAsync(project, launchPorts: false, expected: false);
    }

    private static async Task AssertCertificateSetupAsync(
        string project,
        bool launchPorts,
        bool expected
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "App.csproj"), project);
        if (launchPorts)
        {
            Directory.CreateDirectory(Path.Combine(root.Path, "Properties"));
            await File.WriteAllTextAsync(
                Path.Combine(root.Path, "Properties", "launchSettings.json"),
                """{"profiles":{"app":{"applicationUrl":"https://localhost:8203"}}}"""
            );
        }
        var certificates = SandboxFactory.Certificates();
        certificates
            .PrepareAsync("actual", Arg.Any<CancellationToken>())
            .Returns(
                new SandboxCertificatePreparation(
                    SandboxCertificateStatus.Succeeded,
                    new string('a', 64)
                )
            );
        var exporter = Substitute.For<IHostDevelopmentCertificate>();
        exporter
            .ExportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AboxException("No trusted development certificate."));
        var console = new TestConsole();
        var ports = new AppPorts(
            Substitute.For<IApplicationPortPublisher>(),
            console,
            new SandboxHttps(exporter, certificates, SandboxFactory.Exec(), console)
        );

        // Act
        var action = () => ports.PublishAsync(root.Path, "actual", 10000, CancellationToken.None);

        // Assert
        if (expected)
        {
            await action.Should().ThrowAsync<AboxException>();
            await certificates.Received(1).PrepareAsync("actual", Arg.Any<CancellationToken>());
        }
        else
        {
            await action();
            await certificates
                .DidNotReceive()
                .PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await exporter
                .DidNotReceive()
                .ExportAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }
}
