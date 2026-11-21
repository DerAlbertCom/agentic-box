using Abox.Configuration;
using Abox.Credentials;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Tests;

public sealed class SandboxSquadMetadataTests : IDisposable
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
        _root.File("teams/sample/base.workload", "template: docker.io/sbx/shell-docker-image\n");
        _root.File("teams/sample/ac.abox", "workload: base\nkits:\n  - elastic-a\n  - elastic-b\n");
        foreach (var (kit, yaml) in descriptors)
        {
            _root.File($"kits/{kit}/{kit}.yaml", yaml);
        }

        var local = CredentialRequest.Of(
            new SquadResolver(new AboxConfig()).Resolve(_root.Path, "sample", "ac", [])
        );

        // Act
        var staged = SandboxSquadMetadata.Read(
            "sample-sandbox",
            new SandboxSquadFiles(
                [
                    new StagedKitDescriptor("workload", "schemaVersion: \"3\"\nkind: workload\n"),
                    .. descriptors.Select(kit => new StagedKitDescriptor(kit.Key, kit.Value)),
                ],
                null
            )
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
}
