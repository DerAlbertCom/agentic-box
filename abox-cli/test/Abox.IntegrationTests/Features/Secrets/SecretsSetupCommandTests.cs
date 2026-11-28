using System.Text;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Naming;
using Abox.Features.Shared.Sandboxes;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>
/// The guided secrets setup through <c>abox secrets init</c> and <c>abox secrets update</c>,
/// with substitute host secrets, approvals, sandbox files and published sets.
/// </summary>
public sealed class SecretsSetupCommandTests : IDisposable
{
    private const string Jfrog = "abox-jfrog";
    private const string Context7 = "abox-context7";

    private readonly UserSecretsCliHarness _cli;
    private readonly IPublishedSquadSource _published = Substitute.For<IPublishedSquadSource>();

    public SecretsSetupCommandTests()
        : this(interactive: true) { }

    private SecretsSetupCommandTests(bool interactive)
    {
        _cli = new UserSecretsCliHarness(existingSandbox: false, interactive: interactive);
        _cli.Services.AddSingleton(_published);
    }

    public void Dispose() => _cli.Dispose();

    private static readonly CredentialRequest JfrogRequest = new(
        Jfrog,
        ["jfrog"],
        ["rwest.jfrog.io"],
        Optional: false,
        Description: "JFrog token for the NuGet feeds"
    );

    private static readonly CredentialRequest Context7Request = new(
        Context7,
        ["context7"],
        ["mcp.context7.com"],
        Optional: true
    );

    private static readonly SecretDeclaration JfrogSecret = new(
        Jfrog,
        Inputs:
        [
            new SecretInput("username", "Your JFrog user name", Secret: false),
            new SecretInput("token", "Your JFrog token"),
        ],
        Encoding: SecretEncoding.Base64,
        Description: "JFrog token for the NuGet feeds",
        Docs: "docs/secrets/jfrog.md"
    );

    [Fact]
    public async Task InitOfALocalBuildAsksForTheMissingValueAndApprovalWithoutASandbox()
    {
        // Arrange
        LocalBuild([JfrogRequest], [JfrogSecret]);
        _cli.Console.Texts.Add("albert");
        _cli.Console.Answers.Add("tok");

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Jfrog, Base64("albert:tok"), Arg.Any<CancellationToken>());
        _cli.Approvals.Received(1)
            .Approve(
                Jfrog,
                Arg.Is<IReadOnlyList<string>>(d => d.SequenceEqual(new[] { "rwest.jfrog.io" }))
            );
        var output = _cli.Console.AllText;
        output.Should().Contain("The secrets setup has started: the squad 'ac' needs 1 secret");
        output.Should().Contain("abox-jfrog (jfrog, required)");
        output.Should().Contain("  JFrog token for the NuGet feeds");
        output
            .Should()
            .Contain($"  Documentation: {AboxConfig.DefaultDocsBaseUrl}docs/secrets/jfrog.md");
        output.Should().Contain("Received 3 characters.");
        _cli.Console.Echoes.Should().Equal("albert", "***");
        _cli.Console.Output.Should().Contain(line => line.StartsWith("SERVICE"));
        await _cli
            .Lifecycle.DidNotReceive()
            .CreateAsync(Arg.Any<SandboxDefinition>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitOfAPublishedSquadReadsTheSetWithoutTeamSources()
    {
        // Arrange
        Published([JfrogRequest], [JfrogSecret]);
        _cli.Console.Texts.Add("albert");
        _cli.Console.Answers.Add("tok");

        // Act
        var exitCode = await Init();

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _published
            .Received(1)
            .LoadAsync(
                RegistryImageName.OfSquad(
                    new AboxConfig().PullRepository,
                    new SquadBuildIdentity("sample", "ac", "latest")
                ),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            );
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Jfrog, Base64("albert:tok"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitOfASquadFromAnOlderBuildSaysToRepublishItBeforeAskingEveryPartAsSecret()
    {
        // Arrange
        Published(
            [JfrogRequest],
            [
                JfrogSecret with
                {
                    Inputs = [new SecretInput("username", "User"), new SecretInput("token", null)],
                    OlderBuild = true,
                },
            ]
        );
        _cli.Console.Answers.Add("albert");
        _cli.Console.Answers.Add("tok");

        // Act
        var exitCode = await Init();

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.AllText.Should()
            .Contain(
                "  The squad was built by an older abox, so every part of this secret is asked as a secret "
                    + "and no value is fixed or remembered. Build or publish the squad again with this abox."
            );
        _cli.Console.Echoes.Should().Equal("******", "***");
    }

    [Fact]
    public async Task InitTakesTheTeamAndSquadFromTheFolderConfiguration()
    {
        // Arrange
        Published([JfrogRequest], [JfrogSecret]);
        _cli.Configure("team: sample\nsquad: ac\n");
        _cli.Console.Texts.Add("albert");
        _cli.Console.Answers.Add("tok");

        // Act
        var exitCode = await _cli.Invoke("secrets", "init", "--location", _cli.Location);

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _published
            .Received(1)
            .LoadAsync(
                Arg.Any<string>(),
                new SquadBuildIdentity("sample", "ac", "latest"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task InitSkipsStoredAndApprovedSecrets()
    {
        // Arrange
        LocalBuild([JfrogRequest, Context7Request], [JfrogSecret, new(Context7)]);
        StoreServices(Context7);
        _cli.Approvals.Covers(Context7, Arg.Any<IReadOnlyList<string>>()).Returns(true);
        _cli.Console.Texts.Add("albert");
        _cli.Console.Answers.Add("tok");

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().NotContain(line => line.StartsWith("abox-context7 ("));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Context7, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitWithNothingMissingAsksNothingAndShowsTheTable()
    {
        // Arrange
        LocalBuild([Context7Request], [new(Context7)]);
        StoreServices(Context7);
        _cli.Approvals.Covers(Context7, Arg.Any<IReadOnlyList<string>>()).Returns(true);

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain(line => line.Contains("nothing to set up"));
        _cli.Console.Output.Should()
            .Contain(line => line.StartsWith("abox-context7 ") && line.Contains("stored"));
        _cli.Console.Echoes.Should().BeEmpty();
    }

    [Fact]
    public async Task InitAllAsksForAStoredSecretAgainWithTheRememberedUserNameAsDefault()
    {
        // Arrange
        LocalBuild([JfrogRequest], [JfrogSecret]);
        _cli.Console.Texts.AddRange(["albert", ""]);
        _cli.Console.Answers.AddRange(["first", "second"]);
        await Init("--local");
        StoreServices(Jfrog);
        _cli.Approvals.Covers(Jfrog, Arg.Any<IReadOnlyList<string>>()).Returns(true);

        // Act
        var exitCode = await Init("--local", "--all");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain("Your JFrog user name: [albert] ");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Jfrog, Base64("albert:second"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFixedInputIsNeverAsked()
    {
        // Arrange
        var ado = new SecretDeclaration(
            "abox-azure-devops",
            Inputs:
            [
                new SecretInput("username", null, Value: "pat"),
                new SecretInput("pat", "Azure DevOps PAT"),
            ],
            Encoding: SecretEncoding.Base64
        );
        LocalBuild(
            [
                new CredentialRequest(
                    "abox-azure-devops",
                    ["azure-devops"],
                    ["dev.azure.com"],
                    false
                ),
            ],
            [ado]
        );
        _cli.Approvals.Covers("abox-azure-devops", Arg.Any<IReadOnlyList<string>>()).Returns(true);
        _cli.Console.Answers.Add("the-pat");

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().NotContain(line => line.Contains("username"));
        _cli.Console.Echoes.Should().Equal("*******");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync("abox-azure-devops", Base64("pat:the-pat"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEmptyAnswerSkipsAnOptionalSecret()
    {
        // Arrange
        LocalBuild([Context7Request], [new(Context7)]);

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().Contain("The secret 'abox-context7' was skipped.");
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _cli.Approvals.DidNotReceiveWithAnyArgs().Approve(default!, default!);
    }

    [Fact]
    public async Task ASkippedRequiredSecretFailsInit()
    {
        // Arrange
        LocalBuild([JfrogRequest], [JfrogSecret]);
        _cli.Console.Texts.Add("albert");

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(1);
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConflictingCustomSecretIsReportedAndNotOverwritten()
    {
        // Arrange
        var custom = new SecretDeclaration(
            "abox-custom",
            Hosts: ["api.custom.example.invalid"],
            EnvironmentVariable: "CUSTOM_TOKEN"
        );
        LocalBuild([], [custom]);
        _cli.HostSecrets.ListAsync(Arg.Any<CancellationToken>())
            .Returns(
                new HostSecrets([], [new CustomSecret(["api.custom.example.invalid"], "OTHER")])
            );
        _cli.Console.Answers.Add("the-value");

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should()
            .Contain(error => error.Contains("'abox-custom'") && error.Contains("conflicts with"));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ACommandBackedSecretIsNeverRunButExplained()
    {
        // Arrange
        var command = new SecretDeclaration(
            "abox-command",
            Hosts: ["api.command.example.invalid"],
            EnvironmentVariable: "COMMAND_TOKEN",
            Command: new HostCommand("print-token")
        );
        LocalBuild([], [command]);

        // Act
        var exitCode = await Init("--local");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should().Contain(error => error.Contains("never takes from a sandbox"));
        await _cli
            .HostSecrets.DidNotReceive()
            .SetCustomAsync(
                Arg.Any<SecretDeclaration>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RemoveForgetsTheRememberedUserName()
    {
        // Arrange
        LocalBuild([JfrogRequest], [JfrogSecret]);
        _cli.Console.Texts.AddRange(["albert", ""]);
        _cli.Console.Answers.AddRange(["first", "second"]);
        await Init("--local");
        _cli.ExistingSandbox();
        _cli.SquadFiles.ReadAsync(_cli.Name, Arg.Any<CancellationToken>())
            .Returns(new SandboxSquadFiles([], SquadJson(JfrogSecret)));
        await _cli.Invoke("secrets", "remove", "--location", _cli.Location, "--service", Jfrog);

        // Act
        await Init("--local", "--all");

        // Assert
        _cli.Console.Output.Should().Contain("Your JFrog user name: ");
        _cli.Console.Output.Should().NotContain("Your JFrog user name: [albert] ");
    }

    [Fact]
    public async Task UpdateShowsTheServicesWithTheirStateAndSetsUpOnlyThePickedOnes()
    {
        // Arrange
        SandboxWith(JfrogSecret, new SecretDeclaration(Context7));
        StoreServices(Jfrog);
        _cli.Approvals.Covers(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(true);
        _cli.Console.Selections.Add([0]);
        _cli.Console.Answers.Add("ctx-key");

        // Act
        var exitCode = await Update();

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should()
            .ContainInOrder(
                "Which secrets do you want to set up?",
                "  [ ] abox-context7 (optional, not set)",
                "  [ ] abox-jfrog (stored)"
            );
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Context7, "ctx-key", Arg.Any<CancellationToken>());
        await _cli
            .HostSecrets.DidNotReceive()
            .SetAsync(Jfrog, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWithAServiceSkipsTheListAndAsksForAStoredValueAgain()
    {
        // Arrange
        SandboxWith(JfrogSecret);
        StoreServices(Jfrog);
        _cli.Approvals.Covers(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(true);
        _cli.Console.Texts.Add("albert");
        _cli.Console.Answers.Add("rotated");

        // Act
        var exitCode = await Update("--service", Jfrog);

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        _cli.Console.Output.Should().NotContain("Which secrets do you want to set up?");
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Jfrog, Base64("albert:rotated"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWithoutASandboxUsesTheSquadOfTheFolder()
    {
        // Arrange
        _cli.Configure("team: sample\nsquad: ac\n");
        Published([Context7Request], [new(Context7)]);
        _cli.Console.Selections.Add([0]);
        _cli.Console.Answers.Add("ctx-key");
        _cli.Console.Answer = true;

        // Act
        var exitCode = await Update();

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Context7, "ctx-key", Arg.Any<CancellationToken>());
        await _cli
            .SquadFiles.DidNotReceive()
            .ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateLocalReadsTheLocalBuildOfTheFolderSquadEvenWithASandbox()
    {
        // Arrange
        _cli.Configure("team: sample\nsquad: ac\n");
        SandboxWith(new SecretDeclaration(Context7));
        LocalBuild([JfrogRequest], [JfrogSecret]);
        _cli.Console.Selections.Add([0]);
        _cli.Console.Texts.Add("albert");
        _cli.Console.Answers.Add("tok");

        // Act
        var exitCode = await Update("--local");

        // Assert
        exitCode.Should().Be(0, _cli.Console.AllText);
        await _cli
            .HostSecrets.Received(1)
            .SetAsync(Jfrog, Base64("albert:tok"), Arg.Any<CancellationToken>());
        await _cli
            .SquadFiles.DidNotReceive()
            .ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOfAnUnknownServiceFails()
    {
        // Arrange
        SandboxWith(JfrogSecret);

        // Act
        var exitCode = await Update("--service", "abox-typo");

        // Assert
        exitCode.Should().Be(1);
        _cli.Console.Errors.Should().Contain(error => error.Contains("'abox-typo'"));
    }

    [Theory]
    [InlineData("init")]
    [InlineData("update")]
    public async Task WithoutATerminalTheGuidedSetupFailsNamingSecretsSet(string command)
    {
        // Arrange
        using var test = new SecretsSetupCommandTests(interactive: false);
        test.LocalBuild([JfrogRequest], [JfrogSecret]);
        test.SandboxWith(JfrogSecret);
        string[] args = command == "init" ? ["--team", "sample", "--squad", "ac", "--local"] : [];

        // Act
        var exitCode = await test._cli.Invoke([
            "secrets",
            command,
            "--location",
            test._cli.Location,
            .. args,
        ]);

        // Assert
        exitCode.Should().Be(1);
        test._cli.Console.Errors.Should()
            .Contain(error => error.Contains("needs a terminal"))
            .And.Contain(
                $"  abox secrets set --location \"{test._cli.Location}\" --service {Jfrog}"
            );
        await test
            ._cli.HostSecrets.DidNotReceive()
            .SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private Task<int> Init(params string[] args) =>
        _cli.Invoke([
            "secrets",
            "init",
            "--location",
            _cli.Location,
            "--team",
            "sample",
            "--squad",
            "ac",
            .. args,
        ]);

    private Task<int> Update(params string[] args) =>
        _cli.Invoke(["secrets", "update", "--location", _cli.Location, .. args]);

    private static string Base64(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private void StoreServices(params string[] services) =>
        _cli
            .HostSecrets.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets(services, []));

    /// <summary>A local build of squad <c>ac</c> whose configuration image bundles the secrets.</summary>
    private void LocalBuild(List<CredentialRequest> credentials, List<SecretDeclaration> secrets)
    {
        var build = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            Reference = ImageName.OfTeam("sample", "latest"),
            Credentials = credentials,
            Secrets = secrets,
        };
        LocalWorkloadFactory.WithImages(_cli.Root, build);
        _cli.Provider.GetRequiredService<BuildStore>().Save(build);
    }

    private void Published(List<CredentialRequest> credentials, List<SecretDeclaration> secrets) =>
        _published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new PublishedSquad(
                call.ArgAt<string>(0) + "@" + PublishedSquadFactory.Digest,
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Credentials = credentials,
                    Secrets = secrets,
                }
            ));

    /// <summary>The folder's sandbox, with the jfrog and context7 kits staged and the declarations bundled.</summary>
    private void SandboxWith(params SecretDeclaration[] secrets)
    {
        _cli.ExistingSandbox();
        _cli.SquadFiles.ReadAsync(_cli.Name, Arg.Any<CancellationToken>())
            .Returns(
                new SandboxSquadFiles(
                    [
                        Kit("jfrog", Jfrog, "rwest.jfrog.io", false),
                        Kit("context7", Context7, "mcp.context7.com", true),
                    ],
                    SquadJson(secrets)
                )
            );
    }

    private static StagedKitDescriptor Kit(
        string kit,
        string service,
        string domain,
        bool optional
    ) =>
        new(
            kit,
            "schemaVersion: \"3\"\nkind: mixin\ncapabilities:\n"
                + "  - type: com.docker.sandbox/credential@1\n"
                + $"    optional: {(optional ? "true" : "false")}\n"
                + $"    config:\n      service: {service}\n"
                + $"      apiKey:\n        inject:\n          - domain: {domain}\n"
        );

    private static string SquadJson(params SecretDeclaration[] secrets) =>
        System.Text.Json.JsonSerializer.Serialize(
            new { Secrets = secrets.Select(SquadMetadataSecret.From).ToList() }
        );
}
