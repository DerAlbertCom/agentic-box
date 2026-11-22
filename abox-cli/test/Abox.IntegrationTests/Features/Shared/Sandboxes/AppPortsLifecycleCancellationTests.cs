using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsLifecycleCancellationTests
{
    [Fact]
    public async Task CancellationAfterActivationDoesNotStartVerificationOrLaunch()
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        using var certificate = new LifecycleCertificateCli(fixture.Console);
        using var cancellation = new CancellationTokenSource();
        fixture.Services.AddSingleton<IHostDevelopmentCertificate>(certificate.Exporter);
        fixture.Web();
        fixture
            .Certificates.ActivateAsync(
                Arg.Any<string>(),
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromResult(SandboxFactory.Activated);
            });

        // Act
        var exit = await fixture.CreateShared(cancellation.Token);

        // Assert
        exit.Should().Be(130, fixture.Console.AllText);
        await fixture
            .Certificates.DidNotReceive()
            .VerifyActivationAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.NotContain("Created sandbox '");
    }

    [Theory]
    [InlineData("create")]
    [InlineData("publish")]
    public async Task CancellationAtACompletedStageSavesTheRecordButStartsNoFollowingSetup(
        string stage
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Web();
        if (stage == "create")
            fixture
                .Lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    cancellation.Cancel();
                    return new SandboxCreation("");
                });
        else
            fixture
                .Publisher.PublishAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(_ =>
                {
                    cancellation.Cancel();
                    return Task.CompletedTask;
                });

        // Act
        var exit = await fixture.CreateShared(cancellation.Token);

        // Assert
        exit.Should().Be(130, fixture.Console.AllText);
        fixture.Records.Load(fixture.Name).Should().NotBeNull();
        await fixture
            .Certificates.DidNotReceive()
            .PrepareAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.Contain("abox ports --location")
            .And.NotContain("Created sandbox '");
    }
}
