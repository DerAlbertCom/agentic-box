using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Abox.Tests;

public sealed class SquadCredentialsTests
{
    private readonly TempDirectory _root = new();
    private readonly IHostSecretStore _secrets = TestServiceGraphs.HostSecretStore();
    private readonly ICredentialApprovals _approvals = Substitute.For<ICredentialApprovals>();

    public SquadCredentialsTests()
    {
        _root.File(
            "teams/sample/mti-ca-dotnet.workload",
            "template: docker.io/sbx/shell-docker-image\npublishRepository: test-images-local\n"
        );
        _root.File(
            "kits/github-auth/github-auth.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                optional: true
                config:
                  service: github
                  apiKey:
                    name: GITHUB_TOKEN
                    inject:
                      - domain: api.github.com
                        header: Authorization
            """
        );
        WriteSquad(declared: true);
    }

    private string Location => Path.Combine(_root.Path, "my folder");

    [Fact]
    public async Task WithoutLocalSourcesNothingIsChecked()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        await Credentials(console)
            .PrepareAsync(null, "sample", "ac", Location, CancellationToken.None);

        // Assert
        _secrets.ReceivedCalls().Should().BeEmpty();
        _approvals.ReceivedCalls().Should().BeEmpty();
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task AStoredAndApprovedCredentialAsksNothing()
    {
        // Arrange
        var console = new TestConsole();
        StoreValue();
        _approvals.Covers("github", Domains("api.github.com")).Returns(true);

        // Act
        await Prepare(console);

        // Assert
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingValueIsAskedForAndStored()
    {
        // Arrange
        var console = new TestConsole(answer: true);
        console.Answers.Add("the-token");

        // Act
        await Prepare(console);

        // Assert
        console.AllText.Should().Contain("github").And.Contain("github-auth");
        console.AllText.Should().Contain("api.github.com").And.Contain("not set");
        console.Output.Should().Contain("The secret 'github': ");
        await _secrets.Received(1).SetAsync("github", "the-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyValueIsNotStored()
    {
        // Arrange
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        await _secrets
            .DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        console.AllText.Should().Contain("not set");
    }

    [Fact]
    public async Task ACredentialWithoutAValueIsNotOfferedForApproval()
    {
        // Arrange
        var console = new TestConsole(answer: false);

        // Act
        await Prepare(console);

        // Assert
        console.Output.Should().Contain("Set the secret 'github' now?");
        console.Output.Should().NotContain(line => line.StartsWith("Approve sending"));
        console.Output.Should().Contain("The sandbox starts without the credential 'github'.");
        _approvals.DidNotReceiveWithAnyArgs().Approve(default!, default!);
    }

    [Fact]
    public async Task AMissingCustomSecretNoKitAsksForIsSetFromItsCommand()
    {
        // Arrange
        WriteCommandSecretSquad();
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        console.Output.Should().Contain(line => line.Contains("rwe-ghec (secrets, required)"));
        console.Output.Should().Contain("Set the secret 'rwe-ghec' now?");
        console.Output.Should().NotContain(line => line.StartsWith("Approve sending"));
        await _secrets
            .Received(1)
            .SetCustomAsync(
                Arg.Is<SecretDeclaration>(secret =>
                    secret.Command!.Line == "gh auth token -h rwe.ghe.com"
                    && secret.Hosts!.SequenceEqual(new[] { "rwe.ghe.com", "api.rwe.ghe.com" })
                ),
                null,
                Arg.Any<CancellationToken>()
            );
        console.Output.Should().Contain("Stored the secret 'rwe-ghec'.");
    }

    [Fact]
    public async Task AFailingCommandOfACustomSecretIsShown()
    {
        // Arrange
        WriteCommandSecretSquad();
        _secrets
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new HostSecretChange(false, "command failed: not logged in"));
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        console
            .Errors.Should()
            .Contain("The secret 'rwe-ghec' was not stored: command failed: not logged in");
        console.Output.Should().Contain("The sandbox starts without the credential 'rwe-ghec'.");
    }

    [Fact]
    public async Task AStoredCustomSecretAsksNothing()
    {
        // Arrange
        WriteCommandSecretSquad();
        _secrets
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                new HostSecrets(
                    [],
                    [new CustomSecret(["rwe.ghe.com", "api.rwe.ghe.com"], "RWE_GHE_TOKEN")]
                )
            );
        var console = new TestConsole();

        // Act
        await Prepare(console);

        // Assert
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task AConflictingCustomBindingIsReportedAndNotReplaced()
    {
        // Arrange
        WriteCommandSecretSquad();
        _secrets
            .ListAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets([], [new CustomSecret(["rwe.ghe.com"], "RWE_GHE_TOKEN")]));
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        console
            .Errors.Should()
            .Contain(error => error.Contains("'rwe-ghec'") && error.Contains("conflicts with"));
        console.Output.Should().Contain("The sandbox starts without the credential 'rwe-ghec'.");
        await _secrets
            .DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AServiceTheSquadDoesNotDeclareIsNotAskedFor()
    {
        // Arrange
        WriteSquad(declared: false);
        var console = new TestConsole(answer: true);
        console.Answers.Add("the-token");

        // Act
        await Prepare(console);

        // Assert
        console.Output.Should().NotContain(line => line.StartsWith("The secret"));
        console.AllText.Should().Contain("not declared");
        await _secrets
            .DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAcceptedApprovalIsRecorded()
    {
        // Arrange
        StoreValue();
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        console
            .Output.Should()
            .Contain("Approve sending the credential 'github' to api.github.com?");
        _approvals.Received(1).Approve("github", Domains("api.github.com"));
        console.Output.Should().Contain("Approved the credential 'github' for api.github.com.");
    }

    [Fact]
    public async Task ADeclinedApprovalLeavesTheBindingsAlone()
    {
        // Arrange
        StoreValue();
        var console = new TestConsole(answer: false);

        // Act
        await Prepare(console);

        // Assert
        console
            .Output.Should()
            .Contain("Approve sending the credential 'github' to api.github.com?");
        _approvals.DidNotReceiveWithAnyArgs().Approve(default!, default!);
    }

    [Fact]
    public async Task WithoutATerminalAWarningSaysWhatIsMissingAndNothingIsAsked()
    {
        // Arrange
        var console = new TestConsole(interactive: false, answer: true);
        console.Answers.Add("the-token");

        // Act
        await Prepare(console);

        // Assert
        console.Output.Should().BeEmpty();
        var warning = string.Join(Environment.NewLine, console.Errors);
        warning.Should().Contain("github").And.Contain("not approved");
        warning.Should().Contain($"abox secrets set --location \"{Location}\" --service github");
        warning.Should().Contain("terminal");
        await _secrets
            .DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _approvals.DidNotReceiveWithAnyArgs().Approve(default!, default!);
    }

    [Fact]
    public async Task ACredentialWithoutAnApiKeyIsLeftToSbx()
    {
        // Arrange
        _root.File(
            "kits/github-auth/github-auth.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                config:
                  service: github
            """
        );
        var console = new TestConsole();

        // Act
        await Prepare(console);

        // Assert
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task UnreadableApprovalsAreAWarningNotAFailure()
    {
        // Arrange
        StoreValue();
        _approvals
            .Covers(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Throws(new AboxException("Cannot read the credential approvals: synthetic"));
        var console = new TestConsole();

        // Act
        var prepare = () => Prepare(console);

        // Assert
        await prepare.Should().NotThrowAsync();
        console
            .Errors.Should()
            .Contain(error =>
                error.Contains("were not checked")
                && error.Contains("Cannot read the credential approvals: synthetic")
            );
    }

    private Task Prepare(TestConsole console) =>
        Credentials(console)
            .PrepareAsync(_root.Path, "sample", "ac", Location, CancellationToken.None);

    private SquadCredentials Credentials(TestConsole console) =>
        new(
            new SquadResolver(new AboxConfig(), new YamlDotNetParser()),
            _secrets,
            _approvals,
            TestServiceGraphs.SecretValues(
                AboxPaths.For(_root.Path, _root.Path),
                _secrets,
                console
            ),
            TestServiceGraphs.TokenExpiries(AboxPaths.For(_root.Path, _root.Path), console),
            console,
            new YamlDotNetParser()
        );

    private static IReadOnlyList<string> Domains(params string[] domains) =>
        Arg.Is<IReadOnlyList<string>>(actual => actual.SequenceEqual(domains));

    private void StoreValue() =>
        _secrets.ListAsync(Arg.Any<CancellationToken>()).Returns(new HostSecrets(["github"], []));

    [Fact]
    public async Task WithoutATerminalAMissingGitHubTokenWithParameterHostsStopsTheCreate()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            parameters:
              ghe-host: tenant.ghe.com
            secrets:
              - service: abox-github-enterprise
                host: ["${{ parameters.ghe-host }}", "api.${{ parameters.ghe-host }}"]
                env: GHE_TOKEN
                placeholder: github_pat_{rand}
            """
        );
        var console = new TestConsole(interactive: false);

        // Act
        var act = () => Prepare(console);

        // Assert
        var error = (await act.Should().ThrowAsync<AboxException>()).Which.Message;
        error.Should().Contain("'abox-github-enterprise' for tenant.ghe.com is not stored");
        error.Should().Contain("docs/github-enterprise-token.md");
        console
            .Errors.Should()
            .Contain(line =>
                line.Contains(
                    "abox-github-enterprise (secrets, required) for tenant.ghe.com, api.tenant.ghe.com"
                )
            );
    }

    private void WriteCommandSecretSquad() =>
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            secrets:
              - service: rwe-ghec
                host: [rwe.ghe.com, api.rwe.ghe.com]
                env: RWE_GHE_TOKEN
                command: gh auth token -h rwe.ghe.com
                placeholder: gho_{rand}
            """
        );

    private void WriteSquad(bool declared) =>
        _root.File(
            "teams/sample/ac.abox",
            "workload: mti-ca-dotnet\nkits:\n  - github-auth\n"
                + (declared ? "secrets:\n  - service: github\n" : "")
        );
}
