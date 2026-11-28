using System.Text.Json;
using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Naming;

namespace Abox.Tests;

/// <summary>The secret declarations a build collects from the kits' <c>secrets.yaml</c> into <c>squad.json</c>.</summary>
public sealed class BuildServiceSecretsTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory Root => _fixture.Root;

    private const string JfrogKit = """
        schemaVersion: "3"
        kind: mixin
        capabilities:
          - type: com.docker.sandbox/credential@1
            description: JFrog Artifactory token for the NuGet feeds
            config:
              service: abox-jfrog
              apiKey:
                inject:
                  - domain: rwest.jfrog.io
        """;

    [Fact]
    public async Task TheDeclarationsOfTheKitsGoIntoSquadJsonWithDescriptionDocsAndInputRules()
    {
        // Arrange
        Squad("jfrog", "tools");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File(
            "kits/jfrog/secrets.yaml",
            """
            - service: abox-jfrog
              docs: docs/secrets/jfrog.md
              encoding: base64
              inputs:
                - name: username
                  prompt: Your JFrog user name
                  secret: false
                - name: token
            """
        );
        Root.File("kits/tools/tools.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        Root.File(
            "kits/tools/secrets.yaml",
            """
            - service: abox-tools
              description: The token the setup of the tools reads
              host: api.tools.example.invalid
              env: TOOLS_TOKEN
              command: print-synthetic-token --raw
            - service: abox-ado
              env: ADO_TOKEN
              inputs:
                - name: username
                  value: pat
                - name: pat
            """
        );

        // Act
        await Build();

        // Assert
        AssertJson(
            Secrets(),
            """
            [
              {
                "Service": "abox-jfrog", "Prompt": null, "Hosts": [], "Env": null,
                "Inputs": [
                  {"Name": "username", "Prompt": "Your JFrog user name", "Secret": false, "Value": null},
                  {"Name": "token", "Prompt": null, "Secret": true, "Value": null}
                ],
                "Separator": ":", "Encoding": "base64", "Placeholder": null, "FromCommand": false,
                "Description": "JFrog Artifactory token for the NuGet feeds", "Docs": "docs/secrets/jfrog.md", "ValueCommand": null
              },
              {
                "Service": "abox-tools", "Prompt": null, "Hosts": ["api.tools.example.invalid"], "Env": "TOOLS_TOKEN",
                "Inputs": [], "Separator": ":", "Encoding": "plain", "Placeholder": null, "FromCommand": true,
                "Description": "The token the setup of the tools reads", "Docs": null, "ValueCommand": null
              },
              {
                "Service": "abox-ado", "Prompt": null, "Hosts": [], "Env": "ADO_TOKEN",
                "Inputs": [
                  {"Name": "username", "Prompt": null, "Secret": true, "Value": "pat"},
                  {"Name": "pat", "Prompt": null, "Secret": true, "Value": null}
                ],
                "Separator": ":", "Encoding": "plain", "Placeholder": null, "FromCommand": false,
                "Description": null, "Docs": null, "ValueCommand": null
              }
            ]
            """
        );
        Metadata().GetRawText().Should().NotContain("print-synthetic-token");
    }

    [Fact]
    public async Task TwoKitsMayDeclareAServiceIdentically()
    {
        // Arrange
        Squad("jfrog", "jfrog-copy");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File("kits/jfrog/secrets.yaml", "- service: abox-jfrog\n  encoding: base64\n");
        Root.File("kits/jfrog-copy/jfrog-copy.yaml", JfrogKit);
        Root.File("kits/jfrog-copy/secrets.yaml", "- service: abox-jfrog\n  encoding: base64\n");

        // Act
        await Build();

        // Assert
        Secrets().GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task TwoKitsThatDeclareAServiceDifferentlyFailTheBuild()
    {
        // Arrange
        Squad("jfrog", "jfrog-copy");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File("kits/jfrog/secrets.yaml", "- service: abox-jfrog\n  encoding: base64\n");
        Root.File("kits/jfrog-copy/jfrog-copy.yaml", JfrogKit);
        Root.File("kits/jfrog-copy/secrets.yaml", "- service: abox-jfrog\n");

        // Act
        var build = Build;

        // Assert
        await build
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*'jfrog'*'jfrog-copy'*'abox-jfrog' differently*");
    }

    [Fact]
    public async Task AnUnknownKeyInSecretsYamlFailsTheBuild()
    {
        // Arrange
        Squad("jfrog");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File("kits/jfrog/secrets.yaml", "- service: abox-jfrog\n  promt: typo\n");

        // Act
        var build = Build;

        // Assert
        await build
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*'promt' is not a known key*");
    }

    [Fact]
    public async Task AServiceTheKitDoesNotAskForFailsTheBuild()
    {
        // Arrange
        Squad("jfrog");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File("kits/jfrog/secrets.yaml", "- service: abox-jfrgo\n");

        // Act
        var build = Build;

        // Assert
        await build
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*secrets.yaml*no credential@1 capability*'abox-jfrgo'*");
    }

    [Fact]
    public async Task AFixedInputWithAPromptFailsTheBuild()
    {
        // Arrange
        Squad("jfrog");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File(
            "kits/jfrog/secrets.yaml",
            "- service: abox-jfrog\n  inputs:\n    - name: username\n      value: pat\n      prompt: Enter pat\n"
        );

        // Act
        var build = Build;

        // Assert
        await build.Should().ThrowAsync<AboxException>().WithMessage("*fixed 'value'*");
    }

    [Fact]
    public async Task AValueCommandGoesIntoSquadJsonSoTheSetupCanRunIt()
    {
        // Arrange
        Squad("jfrog");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File(
            "kits/jfrog/secrets.yaml",
            "- service: abox-jfrog\n  valueCommand: gh auth token --hostname rwe.ghe.com\n"
        );

        // Act
        await Build();

        // Assert
        var secret = Secrets()[0];
        secret
            .GetProperty("ValueCommand")
            .GetString()
            .Should()
            .Be("gh auth token --hostname rwe.ghe.com");
        secret.GetProperty("FromCommand").GetBoolean().Should().BeFalse();
    }

    [Theory]
    [InlineData("  prompt: Your token\n")]
    [InlineData("  inputs:\n    - name: token\n")]
    [InlineData("  host: api.example.invalid\n  command: print-token\n")]
    public async Task AValueCommandWithAPromptInputsOrACommandFailsTheBuild(string other)
    {
        // Arrange
        Squad("jfrog");
        Root.File("kits/jfrog/jfrog.yaml", JfrogKit);
        Root.File(
            "kits/jfrog/secrets.yaml",
            "- service: abox-jfrog\n  valueCommand: gh auth token\n" + other
        );

        // Act
        var build = Build;

        // Assert
        await build
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage(
                "*the secret 'abox-jfrog' takes its value from the output of its 'valueCommand', so it has no 'prompt', 'inputs' or 'command'*"
            );
    }

    [Fact]
    public async Task SecretsInASquadConfigurationFailTheBuildWithAPointerToTheKit()
    {
        // Arrange
        Root.File("teams/sample/ac.abox", "workload: sample\nsecrets: [github]\n");

        // Act
        var build = Build;

        // Assert
        await build
            .Should()
            .ThrowAsync<AboxException>()
            .WithMessage("*ac.abox*no longer declares 'secrets'*secrets.yaml*");
    }

    private void Squad(params string[] kits) =>
        Root.File("teams/sample/ac.abox", $"workload: sample\nkits: [{string.Join(", ", kits)}]\n");

    private Task Build() =>
        _fixture.Service.BuildAsync(
            new BuildRequest(
                [_fixture.Resolver.ResolveTeam(Root.Path, "sample")],
                Root.Path,
                "latest"
            ),
            CancellationToken.None
        );

    private JsonElement Metadata()
    {
        var build = _fixture.Store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        var json = File.ReadAllText(Path.Combine(build.ConfigurationMixinDirectory!, "squad.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private JsonElement Secrets() => Metadata().GetProperty("Secrets");

    private static void AssertJson(JsonElement actual, string expectedJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        JsonElement
            .DeepEquals(actual, expected.RootElement)
            .Should()
            .BeTrue("expected {0}, but found {1}", expectedJson, actual.GetRawText());
    }
}
