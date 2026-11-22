using System.ComponentModel;
using Abox.Features.Shared.ApplicationPorts;
using Abox.Features.Shared.Https;
using Abox.Features.Shared.Sandboxes;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsLifecycleFailureTests
{
    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task RequiredSecretCopyFailurePreventsAllLaterPortAndHttpsSetup(string entry)
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Web();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Root.File("appdata/Microsoft/UserSecrets/sample/secrets.json", "{}");
        fixture
            .UserSecrets.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new IOException("private payload")));

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(1, fixture.Console.AllText);
        await fixture
            .Publisher.DidNotReceive()
            .PublishAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .GetDevelopmentCertificatePasswordAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        fixture
            .Console.AllText.Should()
            .Contain("abox user-secrets --location")
            .And.Contain("abox ports --location")
            .And.Contain("has not started")
            .And.NotContain("private payload");
    }

    [Theory]
    [InlineData("published", false)]
    [InlineData("local", false)]
    [InlineData("run", false)]
    [InlineData("published", true)]
    [InlineData("local", true)]
    [InlineData("run", true)]
    public async Task CreationFailureOrCancellationStartsNoPortOrCertificateWork(
        string entry,
        bool cancel
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Web();
        fixture
            .Lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(
                cancel
                    ? Task.FromException<SandboxCreation>(new OperationCanceledException())
                    : Task.FromResult(
                        new SandboxCreation(
                            "synthetic creation failure",
                            "synthetic creation failure"
                        )
                    )
            );

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(cancel ? 130 : 1, fixture.Console.AllText);
        fixture
            .Records.Load(fixture.Name)!
            .BuildCreatedAt.Should()
            .Be(AppPortsLifecycleFixture.OldBuild);
        await fixture
            .Publisher.DidNotReceive()
            .PublishAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .GetDevelopmentCertificatePasswordAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task MissingHostDotnetFailsAfterPublishingWithoutClaimingFullSuccess(string entry)
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Web();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ProcessResult>(new Win32Exception("private child diagnostic"))
            );
        fixture.Services.AddSingleton(new HostDevelopmentCertificate(runner, fixture.Console));

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(1, fixture.Console.AllText);
        fixture
            .Records.Load(fixture.Name)!
            .BuildCreatedAt.Should()
            .BeAfter(AppPortsLifecycleFixture.OldBuild);
        fixture
            .Console.AllText.Should()
            .Contain("Application ports published")
            .And.Contain("Cannot export an exact existing trusted")
            .And.Contain("record retained")
            .And.Contain("abox ports --location")
            .And.NotContain("Created sandbox '")
            .And.NotContain("private child diagnostic");
        await fixture
            .Exec.DidNotReceive()
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("activate", false)]
    [InlineData("verify", false)]
    [InlineData("activate", true)]
    [InlineData("verify", true)]
    public async Task PartialHttpsFailureCanBeRetriedThroughPortsWithoutRecreation(
        string stage,
        bool cancel
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        using var certificate = new LifecycleCertificateCli(fixture.Console);
        fixture.Services.AddSingleton(certificate.Exporter);
        fixture.Existing();
        fixture.Web();
        var failure = Task.FromException(
            cancel
                ? new OperationCanceledException()
                : new AboxException("synthetic activation failure")
        );
        fixture.HttpsOutcome(stage, failure);

        // Act
        var failed = await fixture.Replace("run");

        // Assert
        failed.Should().Be(cancel ? 130 : 1, fixture.Console.AllText);
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.Contain("may remain")
            .And.NotContain("Created sandbox '");
        await fixture
            .Exec.DidNotReceive()
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());

        // Act
        fixture.HttpsOutcome(stage, Task.CompletedTask);
        var retry = await fixture.Invoke(
            "ports",
            "--location",
            fixture.Location,
            "--offset",
            "10000"
        );

        // Assert
        retry.Should().Be(0, fixture.Console.AllText);
        certificate.Exports.Should().Be(2);
        await fixture
            .Lifecycle.Received(1)
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        await fixture
            .Lifecycle.Received(1)
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
