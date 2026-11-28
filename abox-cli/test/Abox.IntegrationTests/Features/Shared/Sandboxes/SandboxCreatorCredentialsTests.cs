using Abox.Features.Shared.Common;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

/// <summary>The guided secrets setup <c>create</c> runs before the sandbox is created.</summary>
public sealed class SandboxCreatorCredentialsTests
{
    private static readonly CredentialRequest Required = new(
        "github",
        ["github-auth"],
        ["api.github.com"],
        Optional: false
    );

    private static readonly CredentialRequest Optional = new(
        "context7",
        ["context7"],
        ["mcp.context7.com"],
        Optional: true
    );

    [Fact]
    public async Task MissingSecretsAreSetUpOnceWithAHeaderBeforeTheExistingSandboxIsRemoved()
    {
        // Arrange
        var fixture = Fixture([Required, Optional]);
        var name = ExistingSandbox(fixture);
        fixture.Console.Answers.AddRange(["the-key", "the-token"]);

        // Act
        var exitCode = await fixture.Service.CreateAsync(fixture.Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        fixture
            .Console.Output.Count(line => line.StartsWith("The secrets setup has started"))
            .Should()
            .Be(1);
        fixture
            .Console.Output.Should()
            .Contain(
                "The secrets setup has started: the squad 'ac' needs 2 secrets that are not stored or approved."
            )
            .And.Contain(line => line.Contains("An empty answer skips an optional secret."));
        Received.InOrder(() =>
        {
            fixture.HostSecrets.SetAsync("context7", "the-key", Arg.Any<CancellationToken>());
            fixture.HostSecrets.SetAsync("github", "the-token", Arg.Any<CancellationToken>());
            fixture.Lifecycle.RemoveAsync(name, Arg.Any<CancellationToken>());
            fixture.Lifecycle.CreateAsync(
                Arg.Any<SandboxDefinition>(),
                Arg.Any<CancellationToken>()
            );
        });
        fixture
            .Approvals.Received(1)
            .Approve(
                "github",
                Arg.Is<IReadOnlyList<string>>(domains =>
                    domains.SequenceEqual(new[] { "api.github.com" })
                )
            );
    }

    [Fact]
    public async Task APreparedHostCreatesWithoutQuestions()
    {
        // Arrange
        var fixture = Fixture([Required]);
        fixture
            .HostSecrets.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets(["github"], []));
        fixture.Approvals.Covers("github", Arg.Any<IReadOnlyList<string>>()).Returns(true);

        // Act
        var exitCode = await fixture.Service.CreateAsync(fixture.Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        fixture.Console.Output.Should().NotContain(line => line.StartsWith("The secrets setup"));
        fixture.Console.Echoes.Should().BeEmpty();
    }

    [Fact]
    public async Task ASkippedRequiredSecretStopsTheCreateBeforeTheExistingSandboxIsReplaced()
    {
        // Arrange
        var fixture = Fixture([Required]);
        ExistingSandbox(fixture);

        // Act
        var create = () => fixture.Service.CreateAsync(fixture.Request(), CancellationToken.None);

        // Assert
        await create
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*required secret 'github'*abox secrets init --location*");
        await fixture
            .Lifecycle.DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture
            .Lifecycle.DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADeclinedApprovalOfARequiredSecretStopsTheCreate()
    {
        // Arrange
        var fixture = Fixture([Required]);
        fixture
            .HostSecrets.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets(["github"], []));
        ExistingSandbox(fixture);
        fixture.Console.Answer = false;

        // Act
        var create = () =>
            fixture.Service.CreateAsync(
                fixture.Request() with
                {
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        await create
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*'github'*not stored or approved*");
        fixture
            .Console.Output.Should()
            .Contain("Approve sending the credential 'github' to api.github.com?");
        fixture.Approvals.DidNotReceiveWithAnyArgs().Approve(default!, default!);
        await fixture
            .Lifecycle.DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutATerminalAMissingRequiredSecretStopsTheCreateBeforeAnythingIsCreated()
    {
        // Arrange
        var fixture = Fixture([Required, Optional], interactive: false);
        ExistingSandbox(fixture);

        // Act
        var create = () =>
            fixture.Service.CreateAsync(
                fixture.Request() with
                {
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        var failure = await create.Should().ThrowAsync<AboxException>();
        failure
            .Which.Message.Should()
            .Contain("without a terminal")
            .And.Contain("github (github-auth, required): value not set, not approved")
            .And.Contain($"abox secrets init --location \"{fixture.Root.Path}\"");
        await fixture
            .Lifecycle.DidNotReceive()
            .RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture
            .Lifecycle.DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
        await fixture
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutATerminalAMissingOptionalSecretIsOneWarningAndTheCreateGoesOn()
    {
        // Arrange
        var fixture = Fixture([Optional], interactive: false);

        // Act
        var exitCode = await fixture.Service.CreateAsync(fixture.Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        fixture
            .Console.Errors.Should()
            .ContainSingle(error => error.StartsWith("Warning: optional secrets"))
            .Which.Should()
            .Contain("context7")
            .And.Contain("abox secrets init --location");
        await fixture
            .Lifecycle.Received(1)
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARefusedReplacementAsksForNoSecrets()
    {
        // Arrange
        var fixture = Fixture([Required]);
        ExistingSandbox(fixture);
        fixture.Console.Answer = false;

        // Act
        var action = () => fixture.Service.CreateAsync(fixture.Request(), CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await fixture.HostSecrets.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheNotesOfSbxAreShownAfterTheSandboxIsCreated()
    {
        // Arrange
        var fixture = Fixture([]);
        fixture
            .Lifecycle.CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>())
            .Returns(
                new SandboxCreation(
                    "Creating sandbox...\n"
                        + "Note: no binding authorizes elastic — the credential was not injected.\n"
                        + "Sandbox created\n"
                )
            );

        // Act
        var exitCode = await fixture.Service.CreateAsync(fixture.Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        fixture
            .Console.Output.Should()
            .Contain("Note: no binding authorizes elastic — the credential was not injected.");
        fixture.Console.Output.Should().NotContain("Creating sandbox...");
    }

    private static SandboxCreatorFixture Fixture(
        List<CredentialRequest> credentials,
        bool interactive = true
    )
    {
        var fixture = new SandboxCreatorFixture(interactive);
        fixture.SaveBuild(
            credentials: credentials,
            secrets: credentials.Select(request => new SecretDeclaration(request.Service)).ToList()
        );
        return fixture;
    }

    private static string ExistingSandbox(SandboxCreatorFixture fixture)
    {
        var name = SandboxName.For(fixture.Root.Path);
        fixture
            .Lifecycle.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        return name;
    }
}
