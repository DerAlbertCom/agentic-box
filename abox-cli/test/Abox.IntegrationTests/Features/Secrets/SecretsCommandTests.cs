using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

/// <summary><c>abox secrets</c> on the sandbox of a folder, with substitute host secrets and squad files.</summary>
public sealed class SecretsCommandTests : IDisposable
{
    private readonly UserSecretsCliHarness _cli = new();

    public void Dispose() => _cli.Dispose();

    [Fact]
    public async Task ListShowsTheServicesTheStagedKitsAskForWithTheirKitsAndWhetherRequired()
    {
        // Arrange
        Sandbox(
            [Kit("jfrog", ("jfrog", false)), Kit("context7", ("context7", true)), Kit("workload")],
            SquadJson("""{"Service":"jfrog"},{"Service":"context7"}""")
        );

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        Line("jfrog").Should().Contain("jfrog").And.Contain("required").And.Contain("not set");
        Line("context7").Should().Contain("optional").And.Contain("not set");
        _cli.Console.AllText.Should().NotContain("not declared");
        await _cli.SquadFiles.Received(1).ReadAsync(_cli.Name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStoppedSandboxIsReadThroughTheExecThatStartsIt()
    {
        // Arrange
        _cli.Lifecycle.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(_cli.Name, "stopped", null) });
        Sandbox([Kit("jfrog", ("jfrog", false))], SquadJson("""{"Service":"jfrog"}"""));

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        Line("jfrog").Should().Contain("required");
        await _cli.SquadFiles.Received(1).ReadAsync(_cli.Name, Arg.Any<CancellationToken>());
        await _cli
            .Exec.DidNotReceive()
            .RunAsync(Arg.Any<SandboxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListShowsOneLinePerServiceWithEveryKitThatAsksForIt()
    {
        // Arrange
        Sandbox(
            [Kit("elastic-a", ("elastic", false)), Kit("elastic-b", ("elastic", true))],
            SquadJson("""{"Service":"elastic"}""")
        );

        // Act
        await Secrets("list");

        // Assert
        _cli.Console.Output.Count(line => line.StartsWith("elastic ", StringComparison.Ordinal))
            .Should()
            .Be(1);
        Line("elastic").Should().Contain("elastic-a, elastic-b").And.Contain("required");
    }

    [Fact]
    public async Task ListShowsStoredServiceCustomAndEnvironmentSecretsAsStored()
    {
        // Arrange
        Sandbox(
            [Kit("github-auth", ("github", false))],
            SquadJson(
                """
                {"Service":"github"},
                {"Service":"custom","Hosts":["api.custom.example.invalid"],"Env":"CUSTOM_TOKEN"},
                {"Service":"env-only","Hosts":[],"Env":"ENV_ONLY_TOKEN"}
                """
            )
        );
        _cli.HostSecrets.ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                new HostSecrets(
                    ["github"],
                    [
                        new CustomSecret(["api.custom.example.invalid"], "CUSTOM_TOKEN"),
                        new CustomSecret([], "ENV_ONLY_TOKEN"),
                    ]
                )
            );

        // Act
        await Secrets("list");

        // Assert
        Line("github").Should().Contain("stored").And.NotContain("not set");
        Line("custom").Should().Contain("stored").And.NotContain("not set");
        Line("env-only").Should().Contain("stored").And.NotContain("not set");
    }

    [Fact]
    public async Task ListMarksAServiceAKitAsksForButTheSquadDoesNotDeclare()
    {
        // Arrange
        Sandbox(
            [Kit("jfrog", ("jfrog", false)), Kit("context7", ("context7", true))],
            SquadJson("""{"Service":"jfrog"}""")
        );

        // Act
        await Secrets("list");

        // Assert
        Line("context7").Should().EndWith("not declared");
        Line("jfrog").Should().NotContain("not declared");
        _cli.Console.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ListShowsACommandBackedBindingAsStoredWhenItHasAllItsHostsAndItsVariable()
    {
        // Arrange
        Sandbox([Kit("rwe-ghec")], SquadJson(RweGhec));
        StoreCustom(
            new CustomSecret(
                [
                    "copilot-api.rwe.ghe.com",
                    "api.rwe.ghe.com",
                    "rwe.ghe.com",
                    "uploads.rwe.ghe.com",
                ],
                "RWE_GHE_TOKEN"
            )
        );

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        Line("rwe-ghec").Should().Contain("stored").And.NotContain("not set");
        Line("rwe-ghec").Should().NotContain("not declared").And.NotContain("conflict");
    }

    [Fact]
    public async Task ListShowsABindingThatCoversOnlySomeHostsAsAConflictNotAsStored()
    {
        // Arrange
        Sandbox([Kit("rwe-ghec")], SquadJson(RweGhec));
        StoreCustom(new CustomSecret(["copilot-api.rwe.ghe.com"], "RWE_GHE_TOKEN"));

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        Line("rwe-ghec").Should().Contain("not set").And.EndWith("conflicts with RWE_GHE_TOKEN");
    }

    [Fact]
    public async Task ListShowsACustomSecretStoredWithoutItsVariableAsStored()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom","Hosts":["custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN"}"""
            )
        );
        StoreCustom(new CustomSecret(["custom.example.invalid"], null));

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        Line("sample-custom").Should().Contain("stored").And.NotContain("not set");
        Line("sample-custom").Should().NotContain("conflict");
    }

    [Fact]
    public async Task SetOfACustomSecretStoredWithoutItsVariableReplacesItInsteadOfReportingAConflict()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom","Hosts":["custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN"}"""
            )
        );
        StoreCustom(new CustomSecret(["custom.example.invalid"], null));
        _cli.Console.Answers.Add("the-value");

        // Act
        var exitCode = await Secrets("set", "--service", "sample-custom");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Errors.Should().NotContain(error => error.Contains("conflicts with"));
        await _cli
            .HostSecrets.Received(1)
            .SetCustomAsync(
                Arg.Is<SecretDeclaration>(secret =>
                    secret.EnvironmentVariable == "SAMPLE_CUSTOM_TOKEN"
                ),
                "the-value",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ListShowsACustomSecretUnderAnotherVariableAsAConflict()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom","Hosts":["custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN"}"""
            )
        );
        StoreCustom(new CustomSecret(["custom.example.invalid"], "OTHER_TOKEN"));

        // Act
        await Secrets("list");

        // Assert
        Line("sample-custom")
            .Should()
            .Contain("not set")
            .And.EndWith("conflicts with OTHER_TOKEN");
    }

    [Fact]
    public async Task ListShowsADeclaredCustomSecretNoKitAsksForAsRequired()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom","Hosts":["custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN"}"""
            )
        );

        // Act
        await Secrets("list");

        // Assert
        Line("sample-custom").Should().MatchRegex(@"^sample-custom\s+-\s+required\s+not set$");
    }

    [Fact]
    public async Task ListWidensItsColumnsToTheLongestServiceName()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom-with-a-long-name","Hosts":["custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN"}"""
            )
        );

        // Act
        await Secrets("list");

        // Assert
        var header = _cli.Console.Output.Single(line =>
            line.StartsWith("SERVICE", StringComparison.Ordinal)
        );
        var row = Line("sample-custom-with-a-long-name");
        row.IndexOf("required", StringComparison.Ordinal)
            .Should()
            .Be(header.IndexOf("NEED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListDoesNotCountACustomSecretOfOtherHostsAsAVariableOnlySecret()
    {
        // Arrange
        Sandbox([Kit("sample-secrets")], SquadJson("""{"Service":"env-only","Env":"ENV_ONLY"}"""));
        StoreCustom(new CustomSecret(["other.example.invalid"], null));

        // Act
        await Secrets("list");

        // Assert
        Line("env-only").Should().Contain("not set").And.NotContain("conflict");
    }

    [Fact]
    public async Task SetOfACommandBackedSecretFromTheSandboxPrintsTheManualInstructionAndRunsNothing()
    {
        // Arrange
        Sandbox([Kit("rwe-ghec")], SquadJson(RweGhec));
        _cli.HostSecrets.ManualCustomSecretInstruction(
                Arg.Is<SecretDeclaration>(secret =>
                    secret.Service == "rwe-ghec"
                    && secret.EnvironmentVariable == "RWE_GHE_TOKEN"
                    && secret.Placeholder == "gho_{rand}"
                ),
                "<host command>"
            )
            .Returns("synthetic store-secret --command '<host command>'");

        // Act
        var exitCode = await Secrets("set", "--service", "rwe-ghec");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain(error =>
                error.Contains("'rwe-ghec' comes from a host command")
                && error.Contains("never takes")
                && error.Contains("team's sources")
                && error.Contains("README")
            );
        _cli.Console.Errors.Should().Contain("  synthetic store-secret --command '<host command>'");
        _cli.Console.Output.Should().NotContain(line => line.EndsWith(": "));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetOfAStoredCommandBackedSecretFromTheSandboxSaysItIsStored()
    {
        // Arrange
        Sandbox([Kit("rwe-ghec")], SquadJson(RweGhec));
        StoreCustom(
            new CustomSecret(
                [
                    "uploads.rwe.ghe.com",
                    "rwe.ghe.com",
                    "api.rwe.ghe.com",
                    "copilot-api.rwe.ghe.com",
                ],
                "RWE_GHE_TOKEN"
            )
        );

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain(line => line.Contains("'rwe-ghec' is stored"));
        _cli.Console.Errors.Should().BeEmpty();
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetReportsAConflictingCommandBackedBindingInsteadOfPrintingTheCommand()
    {
        // Arrange
        Sandbox([Kit("rwe-ghec")], SquadJson(RweGhec));
        StoreCustom(new CustomSecret(["copilot-api.rwe.ghe.com"], "RWE_GHE_TOKEN"));

        // Act
        var exitCode = await Secrets("set", "--service", "rwe-ghec");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain(error =>
                error.Contains("'rwe-ghec'")
                && error.Contains("conflicts with")
                && error.Contains("copilot-api.rwe.ghe.com")
            );
        _cli.HostSecrets.DidNotReceiveWithAnyArgs()
            .ManualCustomSecretInstruction(default!, default!);
    }

    [Fact]
    public async Task SetReportsAConflictingCustomBindingInsteadOfReplacingIt()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom","Hosts":["custom.example.invalid","api.custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN"}"""
            )
        );
        StoreCustom(new CustomSecret(["api.custom.example.invalid"], "OTHER_TOKEN"));

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain(error =>
                error.Contains("'sample-custom'")
                && error.Contains("conflicts with")
                && error.Contains("OTHER_TOKEN")
            );
        _cli.Console.Output.Should().NotContain(line => line.EndsWith(": "));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SetAsksWithTheDeclaredPromptAndStoresAServiceSecret()
    {
        // Arrange
        Sandbox(
            [Kit("cape-elastic", ("cape-elastic", false))],
            SquadJson("""{"Service":"cape-elastic","Prompt":"CAPE Elastic encoded API key"}""")
        );
        _cli.Console.Answers.Add("the-key");

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain("CAPE Elastic encoded API key: ");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("cape-elastic", "the-key", Arg.Any<CancellationToken>());
        _cli.Console.AllText.Should().NotContain("the-key");
    }

    [Fact]
    public async Task SetAsksForEachInputAndStoresTheJoinedEncodedValue()
    {
        // Arrange
        Sandbox(
            [Kit("jfrog", ("jfrog", false))],
            SquadJson(
                """{"Service":"jfrog","Prompt":null,"Hosts":[],"Env":null,"Inputs":[{"Name":"username","Prompt":"JFrog user"},{"Name":"token","Prompt":null}],"Separator":"|","Encoding":"base64"}"""
            )
        );
        _cli.Console.Answers.Add("albert");
        _cli.Console.Answers.Add("s3cret");

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain("JFrog user: ").And.Contain("The token of 'jfrog': ");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("jfrog", "YWxiZXJ0fHMzY3JldA==", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetStoresACustomServiceAsACustomSecretWithItsHostsAndEnvironmentVariable()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets")],
            SquadJson(
                """{"Service":"sample-custom","Prompt":"A custom token","Hosts":["custom.example.invalid","api.custom.example.invalid"],"Env":"SAMPLE_CUSTOM_TOKEN","Inputs":[],"Separator":":","Encoding":"plain"}"""
            )
        );
        _cli.Console.Answers.Add("the-custom-token");

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli
            .HostSecrets.Received(1)
            .SetCustomAsync(
                Arg.Is<SecretDeclaration>(secret =>
                    secret.Service == "sample-custom"
                    && secret.Hosts!.SequenceEqual(
                        new[] { "custom.example.invalid", "api.custom.example.invalid" }
                    )
                    && secret.EnvironmentVariable == "SAMPLE_CUSTOM_TOKEN"
                    && secret.Command == null
                ),
                "the-custom-token",
                Arg.Any<CancellationToken>()
            );
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _cli.Console.Output.Should().NotContain(line => line.Contains("no kit of the sandbox"));
    }

    [Fact]
    public async Task AnEmptyAnswerStoresNothing()
    {
        // Arrange
        Sandbox(
            [Kit("sample-secrets", ("sample-token", false), ("sample-optional", true))],
            SquadJson("""{"Service":"sample-token"},{"Service":"sample-optional"}""")
        );
        _cli.Console.Answers.Add("the-token");
        _cli.Console.Answers.Add("");

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(1);
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("sample-token", "the-token", Arg.Any<CancellationToken>());
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync("sample-optional", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _cli.Console.Errors.Should().Contain("The service 'sample-optional' was not set.");
    }

    [Fact]
    public async Task SetWithServiceAsksOnlyForTheNamedServices()
    {
        // Arrange
        Sandbox(
            [Kit("kits", ("github", false), ("jfrog", false), ("elastic", false))],
            SquadJson("""{"Service":"github"},{"Service":"jfrog"},{"Service":"elastic"}""")
        );
        _cli.Console.Answers.Add("one");
        _cli.Console.Answers.Add("two");

        // Act
        var exitCode = await Secrets("set", "--service", "jfrog", "--service", "github");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli.HostSecrets.Received(1).SetAsync("jfrog", "one", Arg.Any<CancellationToken>());
        await _cli.HostSecrets.Received(1).SetAsync("github", "two", Arg.Any<CancellationToken>());
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync("elastic", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetOfADeclaredServiceNoKitAsksForPrintsANote()
    {
        // Arrange
        Sandbox([Kit("workload")], SquadJson("""{"Service":"github"}"""));
        _cli.Console.Answers.Add("the-token");

        // Act
        var exitCode = await Secrets("set", "--service", "github");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should()
            .Contain("Note: no kit of the sandbox asks for the service 'github'.");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("github", "the-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetOfAnUndeclaredServiceAKitAsksForUsesTheDefaultPrompt()
    {
        // Arrange
        Sandbox([Kit("context7", ("context7", true))], SquadJson(""));
        _cli.Console.Answers.Add("the-key");

        // Act
        var exitCode = await Secrets("set", "--service", "context7");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain("The secret 'context7': ");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("context7", "the-key", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetOfAServiceNeitherDeclaredNorAskedForFailsWithoutAsking()
    {
        // Arrange
        Sandbox([Kit("workload")], SquadJson("""{"Service":"github"}"""));

        // Act
        var exitCode = await Secrets("set", "--service", "sample-custom");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain(error =>
                error.Contains("'sample-custom' is not declared")
                && error.Contains("no kit of the sandbox asks for it")
            );
        _cli.Console.Output.Should().NotContain(line => line.EndsWith(": "));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveUsesTheServiceHostOrEnvironmentVariableIdentity()
    {
        // Arrange
        Sandbox(
            [Kit("kits", ("github", false))],
            SquadJson(
                """
                {"Service":"github"},
                {"Service":"custom","Hosts":["api.custom.example.invalid"],"Env":"CUSTOM_TOKEN"},
                {"Service":"env-only","Hosts":[],"Env":"ENV_ONLY_TOKEN"}
                """
            )
        );

        // Act
        var exitCode = await Secrets("remove");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli
            .HostSecrets.Received(1)
            .RemoveAsync(
                new SecretIdentity(SecretIdentityKind.Service, "github"),
                Arg.Any<CancellationToken>()
            );
        await _cli
            .HostSecrets.Received(1)
            .RemoveAsync(
                new SecretIdentity(SecretIdentityKind.Host, "api.custom.example.invalid"),
                Arg.Any<CancellationToken>()
            );
        await _cli
            .HostSecrets.Received(1)
            .RemoveAsync(
                new SecretIdentity(SecretIdentityKind.EnvironmentVariable, "ENV_ONLY_TOKEN"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemoveWithServiceRemovesOnlyTheNamedService()
    {
        // Arrange
        Sandbox(
            [Kit("kits", ("github", false))],
            SquadJson(
                """{"Service":"github"},{"Service":"custom","Hosts":["api.custom.example.invalid"],"Env":"CUSTOM_TOKEN"}"""
            )
        );

        // Act
        var exitCode = await Secrets("remove", "--service", "custom");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli
            .HostSecrets.Received(1)
            .RemoveAsync(
                new SecretIdentity(SecretIdentityKind.Host, "api.custom.example.invalid"),
                Arg.Any<CancellationToken>()
            );
        await _cli
            .HostSecrets.DidNotReceive()
            .RemoveAsync(
                new SecretIdentity(SecretIdentityKind.Service, "github"),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnOlderSandboxListsItsKitServicesAsNotDeclaredWithARecreateHint(
        bool withSquadJson
    )
    {
        // Arrange
        Sandbox(
            [Kit("jfrog", ("jfrog", false))],
            withSquadJson ? """{"SchemaVersion":1,"Team":"sample","Squad":"ac"}""" : null
        );

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        Line("jfrog").Should().EndWith("not declared");
        _cli.Console.Errors.Should()
            .ContainSingle(error =>
                error.Contains("no secret declarations")
                && error.Contains("recreate the sandbox from a current build or publication")
            );
    }

    [Fact]
    public async Task AnOlderSandboxSetsAServiceItsKitsAskForWithTheDefaultPrompt()
    {
        // Arrange
        Sandbox([Kit("jfrog", ("jfrog", false))], null);
        _cli.Console.Answers.Add("the-value");

        // Act
        var exitCode = await Secrets("set", "--service", "jfrog");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain("The secret 'jfrog': ");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("jfrog", "the-value", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOlderSandboxCannotSetACustomServiceAndSaysHowToFixIt()
    {
        // Arrange
        Sandbox([Kit("jfrog", ("jfrog", false))], null);

        // Act
        var exitCode = await Secrets("set", "--service", "sample-custom");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain(error => error.Contains("'sample-custom' is not declared"))
            .And.Contain(error =>
                error.Contains("recreate the sandbox from a current build or publication")
            );
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnOlderSandboxHasNothingToSetWithoutANamedService()
    {
        // Arrange
        Sandbox([Kit("jfrog", ("jfrog", false))], null);

        // Act
        var exitCode = await Secrets("set");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain("There are no secrets to set.")
            .And.Contain(error => error.Contains("--service <name>"));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("""{"Secrets":{"Service":"github"}}""")]
    [InlineData("""{"Secrets":[{"Prompt":"no service"}]}""")]
    [InlineData("""{"Secrets":[{"Service":"jfrog","Encoding":"rot13"}]}""")]
    [InlineData("""{"Secrets":[{"Service":"jfrog","Inputs":[{"Prompt":"no name"}]}]}""")]
    [InlineData("""{"Secrets":[{"Service":"jfrog"},{"Service":"jfrog"}]}""")]
    [InlineData("""{"Secrets":[""")]
    [InlineData(
        """{"Secrets":[{"Service":"rwe-ghec","Env":"RWE_GHE_TOKEN","FromCommand":true}]}"""
    )]
    public async Task MalformedSecretsFailNamingTheSandboxAndSuggestARebuild(string squadJson)
    {
        // Arrange
        Sandbox([Kit("jfrog", ("jfrog", false))], squadJson);

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .ContainSingle(error =>
                error.Contains($"'{_cli.Name}'")
                && error.Contains("squad.json")
                && error.Contains("Rebuild or republish the squad")
            );
        await _cli.HostSecrets.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMalformedStagedDescriptorFailsNamingTheSandbox()
    {
        // Arrange
        Sandbox([new StagedKitDescriptor("broken", "capabilities: [")], SquadJson(""));

        // Act
        var exitCode = await Secrets("list");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .ContainSingle(error =>
                error.Contains(_cli.Name)
                && error.Contains("/usr/share/sandbox/kit/broken/kit.yaml")
            );
    }

    private const string RweGhec = """
        {"Service":"rwe-ghec","Prompt":null,"Hosts":["copilot-api.rwe.ghe.com","api.rwe.ghe.com","rwe.ghe.com","uploads.rwe.ghe.com"],"Env":"RWE_GHE_TOKEN","Inputs":[],"Separator":":","Encoding":"plain","Placeholder":"gho_{rand}","FromCommand":true}
        """;

    private void StoreCustom(params CustomSecret[] custom) =>
        _cli
            .HostSecrets.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets([], custom));

    private Task<int> Secrets(params string[] args) =>
        _cli.Invoke(["secrets", .. args, "--location", _cli.Location]);

    private string Line(string service) =>
        _cli.Console.Output.Single(line =>
            line.StartsWith(service + " ", StringComparison.Ordinal)
        );

    private void Sandbox(IReadOnlyList<StagedKitDescriptor> kits, string? squadJson) =>
        _cli
            .SquadFiles.ReadAsync(_cli.Name, Arg.Any<CancellationToken>())
            .Returns(new SandboxSquadFiles(kits, squadJson));

    private static string SquadJson(string secrets) =>
        $$"""
            {"SchemaVersion":1,"Team":"sample","Squad":"ac","Tag":"latest","CreatedAt":"2026-11-21T00:00:00+00:00","Mcp":[],"State":[],"Kits":[],"Secrets":[{{secrets}}]}
            """;

    private static StagedKitDescriptor Kit(
        string name,
        params (string Service, bool Optional)[] services
    )
    {
        var capabilities =
            services.Length == 0
                ? " []"
                : string.Concat(
                    services.Select(service =>
                        $"\n  - type: com.docker.sandbox/credential@1"
                        + $"\n    optional: {(service.Optional ? "true" : "false")}"
                        + $"\n    config:\n      service: {service.Service}\n      phase: runtime"
                        + $"\n      apiKey:\n        inject:\n          - domain: {service.Service}.example.invalid"
                    )
                );
        return new(name, $"schemaVersion: \"3\"\nkind: mixin\ncapabilities:{capabilities}\n");
    }
}
