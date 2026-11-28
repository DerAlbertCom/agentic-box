using Abox.Features.Secrets;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Credentials;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class SquadSecretsTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void StagedDescriptorsGiveTheSameCredentialRequestsAsTheLocalKitFolders()
    {
        // Arrange
        var descriptors = new Dictionary<string, string>
        {
            ["elastic-a"] = """
                schemaVersion: "3"
                kind: mixin
                capabilities:
                  - type: com.docker.sandbox/credential@1
                    config:
                      service: elastic
                      apiKey:
                        inject:
                          - domain: a.elastic.example.invalid
                  - type: com.docker.sandbox/credential@1
                    optional: true
                    config:
                      service: context7
                """,
            ["elastic-b"] = """
                schemaVersion: "3"
                kind: mixin
                capabilities:
                  - type: com.docker.sandbox/network-policy@1
                    config:
                      runtime:
                        allow: [b.elastic.example.invalid]
                  - type: com.docker.sandbox/credential@1
                    optional: true
                    config:
                      service: elastic
                      apiKey:
                        inject:
                          - domain: b.elastic.example.invalid
                          - domain: a.elastic.example.invalid
                """,
        };
        _root.File(
            "teams/sample/base.workload",
            "template: docker.io/sbx/shell-docker-image\npublishRepository: test-images-local\n"
        );
        _root.File("teams/sample/ac.abox", "workload: base\nkits:\n  - elastic-a\n  - elastic-b\n");
        foreach (var (kit, yaml) in descriptors)
        {
            _root.File($"kits/{kit}/{kit}.yaml", yaml);
        }

        var local = CredentialRequest.Of(
            new SquadResolver(new AboxConfig(), new YamlDotNetParser()).Resolve(
                _root.Path,
                "sample",
                "ac"
            ),
            new YamlDotNetParser()
        );

        // Act
        var staged = SquadSecrets.Read(
            "sample-sandbox",
            new SandboxSquadFiles(
                [
                    new StagedKitDescriptor("workload", "schemaVersion: \"3\"\nkind: workload\n"),
                    .. descriptors.Select(kit => new StagedKitDescriptor(kit.Key, kit.Value)),
                ],
                null
            ),
            new YamlDotNetParser()
        );

        // Assert
        staged.Requests.Should().BeEquivalentTo(local, options => options.WithStrictOrdering());
        staged
            .Requests.Should()
            .BeEquivalentTo(
                new[]
                {
                    new CredentialRequest(
                        "elastic",
                        ["elastic-a", "elastic-b"],
                        ["a.elastic.example.invalid", "b.elastic.example.invalid"],
                        Optional: false
                    ),
                    new CredentialRequest("context7", ["elastic-a"], [], Optional: true),
                },
                options => options.WithStrictOrdering()
            );
    }

    [Fact]
    public void AnOlderSquadJsonWithoutTheNewFieldsReadsAsSecretInputsWithoutDescriptionOrDocs()
    {
        // Arrange
        const string json = """
            {"Secrets":[{"Service":"abox-jfrog","Prompt":null,"Hosts":[],"Env":null,"Inputs":[{"Name":"username","Prompt":"User"},{"Name":"token","Prompt":null}],"Separator":":","Encoding":"base64","Placeholder":null,"FromCommand":false}]}
            """;

        // Act
        var squad = SquadSecrets.Read(
            "older-sandbox",
            new SandboxSquadFiles([], json),
            new YamlDotNetParser()
        );

        // Assert
        squad
            .Secrets.Should()
            .BeEquivalentTo(
                new[]
                {
                    new SecretDeclaration(
                        "abox-jfrog",
                        Inputs:
                        [
                            new SecretInput("username", "User"),
                            new SecretInput("token", null),
                        ],
                        Encoding: SecretEncoding.Base64,
                        OlderBuild: true
                    ),
                }
            );
        squad.Secrets[0].Inputs!.Should().OnlyContain(input => input.Secret && input.Value == null);
        squad.Secrets[0].Description.Should().BeNull();
        squad.Secrets[0].Docs.Should().BeNull();
    }
}
