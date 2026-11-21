using Abox.Configuration;
using Abox.Credentials;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SquadCredentialsTests
{
    private readonly TempDirectory _root = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();

    public SquadCredentialsTests()
    {
        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>()).Returns(new HostSecrets([], []));
        _sbx.SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.SetCustomSecretAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SbxOutcome(0, string.Empty));

        _root.File(
            "teams/sample/mti-ca-dotnet.workload",
            "template: docker.io/sbx/shell-docker-image\n"
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

    private string BindingsFile => Path.Combine(_root.Path, "sbx", "credentials.yaml");

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
        _sbx.ReceivedCalls().Should().BeEmpty();
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task AStoredAndApprovedCredentialAsksNothing()
    {
        // Arrange
        var console = new TestConsole();
        StoreValue();
        new SbxCredentialBindings(BindingsFile).Approve("github", ["api.github.com"]);

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
        await _sbx.Received(1).SetSecretAsync("github", "the-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyValueIsNotStored()
    {
        // Arrange
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        await _sbx.DidNotReceive()
            .SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        File.Exists(BindingsFile).Should().BeFalse();
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
        await _sbx.Received(1)
            .SetCustomSecretAsync(
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
        _sbx.SetCustomSecretAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SbxOutcome(1, "command failed: not logged in\n"));
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
        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>())
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
        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets([], [new CustomSecret(["rwe.ghe.com"], "RWE_GHE_TOKEN")]));
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        console
            .Errors.Should()
            .Contain(error => error.Contains("'rwe-ghec'") && error.Contains("conflicts with"));
        console.Output.Should().Contain("The sandbox starts without the credential 'rwe-ghec'.");
        await _sbx.DidNotReceive()
            .SetCustomSecretAsync(
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
        await _sbx.DidNotReceive()
            .SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnApprovalIsAddedToTheBindingsOfSbx()
    {
        // Arrange
        StoreValue();
        _root.File(
            "sbx/credentials.yaml",
            """
            bindings:
                elastic:
                    apiKey:
                        domains:
                            - api.elastic.co
            """
        );
        var console = new TestConsole(answer: true);

        // Act
        await Prepare(console);

        // Assert
        console
            .Output.Should()
            .Contain("Approve sending the credential 'github' to api.github.com?");
        var bindings = new SbxCredentialBindings(BindingsFile);
        bindings.Covers("github", ["api.github.com"]).Should().BeTrue();
        bindings.Covers("elastic", ["api.elastic.co"]).Should().BeTrue();
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
        File.Exists(BindingsFile).Should().BeFalse();
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
        await _sbx.DidNotReceive()
            .SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        File.Exists(BindingsFile).Should().BeFalse();
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
    public async Task ABrokenBindingsFileIsAWarningNotAFailure()
    {
        // Arrange
        StoreValue();
        _root.File("sbx/credentials.yaml", "bindings: [github]\n");
        var console = new TestConsole();

        // Act
        var prepare = () => Prepare(console);

        // Assert
        await prepare.Should().NotThrowAsync();
        console.Errors.Should().Contain(error => error.Contains(BindingsFile));
    }

    private Task Prepare(TestConsole console) =>
        Credentials(console)
            .PrepareAsync(_root.Path, "sample", "ac", Location, CancellationToken.None);

    private SquadCredentials Credentials(TestConsole console) =>
        new(
            new SquadResolver(new AboxConfig()),
            new PersonalConfigurations(
                AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path)
            ),
            _sbx,
            new SbxCredentialBindings(BindingsFile),
            new SecretValues(_sbx, console),
            console
        );

    private void StoreValue() =>
        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets(["github"], []));

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
