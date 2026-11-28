using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;

namespace Abox.Tests;

/// <summary>The secret declarations of this repository's own teams, which live in their kits.</summary>
public sealed class RepositorySquadSecretsTests
{
    private static readonly string Root = FindRepository();

    private readonly SquadResolver _resolver = new(new AboxConfig(), new YamlDotNetParser());

    public static TheoryData<string, string> Squads()
    {
        var data = new TheoryData<string, string>();
        foreach (var team in new[] { "mti-ca", "sample" })
        {
            foreach (
                var squad in Directory.EnumerateFiles(Path.Combine(Root, "teams", team), "*.abox")
            )
            {
                data.Add(team, Path.GetFileNameWithoutExtension(squad));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Squads))]
    public void EverySquadOfTheRepositoryResolvesItsSecretsFromItsKits(string team, string squad)
    {
        // Act
        var resolved = _resolver.Resolve(Root, team, squad);

        // Assert
        resolved.Secrets.Should().OnlyHaveUniqueItems(secret => secret.Service);
        resolved
            .Secrets.Where(secret => !secret.IsCustom)
            .Should()
            .OnlyContain(secret => secret.Description != null);
    }

    [Fact]
    public void TheAzureDevOpsUserNameIsFixedAndTheJfrogUserNameIsEchoed()
    {
        // Act
        var cape = _resolver.Resolve(Root, "mti-ca", "cape");

        // Assert
        var ado = cape.Secrets.Single(secret => secret.Service == "abox-azure-devops");
        ado.Inputs!.Should()
            .ContainSingle(input => input.Name == "username" && input.Value == "pat");
        var jfrog = cape.Secrets.Single(secret => secret.Service == "abox-jfrog");
        jfrog.Inputs!.Should().ContainSingle(input => input.Name == "username" && !input.Secret);
        jfrog.Docs.Should().Be("docs/secrets/jfrog.md");
    }

    [Fact]
    public void EveryDocumentationPathOfAKitPointsToAPageOfTheRepository()
    {
        // Arrange
        var kits = Directory.EnumerateDirectories(Path.Combine(Root, "kits"));

        // Act
        var docs = kits.SelectMany(kit => KitSecrets.Load(kit, new YamlDotNetParser()))
            .Select(secret => secret.Docs)
            .OfType<string>()
            .Distinct()
            .ToList();

        // Assert
        docs.Should().NotBeEmpty();
        docs.Should().OnlyContain(path => File.Exists(Path.Combine(Root, path)));
    }

    private static string FindRepository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !Directory.Exists(Path.Combine(directory.FullName, "teams", "mti-ca"))
        )
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "The repository with the teams folder was not found."
            );
    }
}
