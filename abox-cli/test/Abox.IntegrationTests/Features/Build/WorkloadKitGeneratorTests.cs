using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class WorkloadKitGeneratorTests
{
    private readonly TempDirectory _root = new();
    private readonly WorkloadKitGenerator _generator = new();

    [Fact]
    public void TheApprovedCorporateCasAreEmbeddedAndCopiedExactlyIntoTheBuildContext()
    {
        // Arrange
        // Security-integrity pin: trust-anchor changes require explicit approval, unlike static snapshots.
        var approved = new Dictionary<string, string>
        {
            ["rwe-private-auth-ica-2024.crt"] =
                "3BD5E6B01CC00F3F8153FC6ABB4CE8DCAB0F0E9BDCEAA729C9A04AC1F8A3DACD",
            ["rwe-private-root-ca-r1.crt"] =
                "816062A7D057D5388511010C6E7C0A40C12641FBA9570796DAD79AF0F88C30F5",
            ["rwe-private-root-ca-r23.crt"] =
                "6592FA28B2D9CFFF827E65B6674604CE68DF9D9AFC90CAFBAAD2726D423AA7BE",
            ["rwe-private-zica-2020.crt"] =
                "D8FD53608F041F67CF4412494216FC466FC64201EE73982D78E30145E69E03D2",
            ["rwe-root-ca-corporate-2016.crt"] =
                "0C7A731EE70D1A03CA93897BC10697BB98475872BA8FC7ECCDEA700673046833",
        };
        var directory = Path.Combine(_root.Path, "kit", "certificates");

        // Act
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        // Assert
        var files = Directory.GetFiles(directory).Select(Path.GetFileName).ToList();
        files.Should().BeEquivalentTo(approved.Keys);
        foreach (var (fileName, fingerprint) in approved)
        {
            using var certificate = X509Certificate2.CreateFromPem(
                File.ReadAllText(Path.Combine(directory, fileName))
            );
            certificate.GetCertHashString(HashAlgorithmName.SHA256).Should().Be(fingerprint);
        }
    }

    [Fact]
    public void EachTrustedCertificateIsOneCaWithoutAPrivateKey()
    {
        // Arrange
        var directory = Path.Combine(_root.Path, "kit", "certificates");

        // Act
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        // Assert
        var files = Directory.GetFiles(directory, "*.crt");
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            // update-ca-certificates reads one certificate per .crt file.
            var pem = File.ReadAllText(file);
            pem.Should().NotContain("PRIVATE KEY").And.NotContain("\r");
            Regex.Matches(pem, "-----BEGIN CERTIFICATE-----").Should().HaveCount(1);
            using var certificate = X509Certificate2.CreateFromPem(pem);
            certificate.HasPrivateKey.Should().BeFalse();
            certificate
                .Extensions.OfType<X509BasicConstraintsExtension>()
                .Should()
                .ContainSingle(constraints => constraints.CertificateAuthority);
        }

        // Arrange
        File.WriteAllText(Path.Combine(directory, "stale.crt"), "not an approved CA");

        // Act
        _generator.Generate(WriteWorkload(""), Path.Combine(_root.Path, "kit"));

        // Assert
        File.Exists(Path.Combine(directory, "stale.crt")).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("apt: [git]\n")]
    [InlineData("mise: [nodejs@24.4.3]\n")]
    [InlineData("scripts: [setup.sh]\n")]
    public void CertificateTrustIsInstalledAsRootBeforeAnyDownloadsAndRetainedForTheAgent(
        string configuration
    )
    {
        // Arrange
        _root.File("setup.sh", "echo setup");

        // Act
        _generator.Generate(WriteWorkload(configuration), Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"))
            .ReplaceLineEndings("\n");
        dockerfile
            .Should()
            .Contain(
                "USER root\nCOPY certificates/ /usr/local/share/ca-certificates/abox/\nRUN update-ca-certificates"
            );
        dockerfile
            .Should()
            .Contain(
                "RUN update-ca-certificates\n"
                    + "ENV MISE_EXTRA_CA_CERTS=\"/etc/ssl/certs/ca-certificates.crt\"\n"
                    + "ENV NODE_EXTRA_CA_CERTS=\"/etc/ssl/certs/ca-certificates.crt\"\n"
                    + "ENV REQUESTS_CA_BUNDLE=\"/etc/ssl/certs/ca-certificates.crt\"\n"
                    + "ENV AWS_CA_BUNDLE=\"/etc/ssl/certs/ca-certificates.crt\"\n"
                    + "ENV UV_SYSTEM_CERTS=\"true\"\n"
                    + "USER agent\n"
            );
        var trust = dockerfile.IndexOf("RUN update-ca-certificates", StringComparison.Ordinal);
        foreach (var download in new[] { "apt-get update", "curl -fsSL", "mise use", "RUN bash" })
        {
            var position = dockerfile.IndexOf(download, StringComparison.Ordinal);
            if (position >= 0)
            {
                trust.Should().BeLessThan(position);
            }
        }

        Regex.Matches(dockerfile, @"(?m)^USER ([^\n]+)")[^1].Groups[1].Value.Should().Be("agent");
        dockerfile.Should().NotContain("--insecure").And.NotContain("SSL_VERIFY=false");
        if (configuration.Length == 0 || configuration.StartsWith("apt:"))
        {
            dockerfile.Should().NotContain("mise.run");
        }
    }

    [Fact]
    public void TheImageIsBuiltOnTheTemplateOfTheWorkload()
    {
        // Arrange
        var workload = WriteWorkload("template: docker.io/sbx/shell-image\n");

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("FROM docker.io/sbx/shell-image");
    }

    [Fact]
    public void AWorkloadWithoutATemplateUsesTheDefaultOne()
    {
        // Arrange
        var workload = WriteWorkload("mise: []\n");

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain($"FROM {WorkloadFile.DefaultTemplate}");
    }

    [Fact]
    public void TheAptPackagesAreInstalledInOneStepBeforeTheTools()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            template: docker.io/sbx/shell-docker-image
            apt:
              - git
              - jq
            mise:
              - nodejs@24.4.3
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("apt-get update");
        dockerfile.Should().Contain("apt-get install -y --no-install-recommends");
        dockerfile.Should().Contain("    git \\");
        dockerfile.Should().Contain("    jq \\");
        dockerfile.Should().Contain("apt-get clean");
        TestText.CountOccurrences(dockerfile, "apt-get update").Should().Be(1);
        dockerfile
            .IndexOf("jq", StringComparison.Ordinal)
            .Should()
            .BeLessThan(dockerfile.IndexOf("nodejs@24.4.3", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePackagesAreInstalledAsRootAndTheToolsAsTheAgentUser()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            apt:
              - git
            mise:
              - nodejs@24.4.3
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile = dockerfile.ReplaceLineEndings("\n");
        foreach (
            var (instruction, user) in new[]
            {
                ("apt-get install", "root"),
                ("curl -fsSL https://mise.run", "agent"),
                ("mise use -g -y nodejs@24.4.3", "agent"),
            }
        )
        {
            var position = dockerfile.IndexOf(instruction, StringComparison.Ordinal);
            position.Should().BeGreaterThan(0);
            var activeUser = Regex
                .Matches(dockerfile[..position], @"(?m)^USER ([^\n]+)")
                .LastOrDefault()
                ?.Groups[1]
                .Value;
            activeUser.Should().Be(user, $"{instruction} must run as {user}");
        }
    }

    [Fact]
    public void ANameThatIsNoPackageNameIsAnError()
    {
        // Arrange: the workload text includes shell syntax in a package name.

        // Act
        var write = () =>
            WriteWorkload(
                """
                apt:
                  - "git; rm -rf /"
                """
            );

        // Assert
        write.Should().Throw<AboxException>().WithMessage("*not a package name*");
    }

    [Fact]
    public void EveryToolHasItsOwnInstallStep()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("mise use -g -y nodejs@24.4.3");
        dockerfile.Should().Contain("mise use -g -y dotnet@10.0.403");
        TestText.CountOccurrences(dockerfile, "mise use -g -y").Should().Be(2);
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AFailedMiseDownloadStopsTheBootstrap()
    {
        // Arrange: the synthetic shell functions make the download fail.

        // Act
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() { echo 'curl: (60) SSL certificate problem' >&2; return 60; }
            """
        );

        // Assert
        result.StandardError.Should().Contain("SSL certificate problem");
        result
            .ExitCode.Should()
            .Be(
                60,
                $"a failed download must not reach the next RUN without mise: {result.StandardError}"
            );
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task ASuccessfulInstallerMustLeaveMiseRunnable()
    {
        // Arrange: the synthetic installer succeeds without installing mise.

        // Act
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() {
                while [ "$1" != '-o' ]; do shift; done
                printf '%s\n' 'exit 0' > "$2"
            }
            mise() { echo 'mise: not found' >&2; return 127; }
            """
        );

        // Assert
        result
            .ExitCode.Should()
            .Be(127, "bootstrap must verify that mise is runnable before installing tools");
        result.StandardError.Should().Contain("mise: not found");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AFailedMiseInstallerStopsTheBootstrap()
    {
        // Arrange: the synthetic installer exits unsuccessfully.

        // Act
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() {
                while [ "$1" != '-o' ]; do shift; done
                printf '%s\n' 'exit 23' > "$2"
            }
            mise() { echo 'tools must not run' >&2; return 0; }
            """
        );

        // Assert
        result.ExitCode.Should().Be(23);
        result.StandardError.Should().NotContain("tools must not run");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task AnExistingMiseDoesNotNeedADownload()
    {
        // Arrange: mise is available through a synthetic shell function.

        // Act
        var result = await RunBootstrapAsync(
            """
            curl() { echo 'download must not run' >&2; return 60; }
            mise() { echo 'mise test version'; }
            """
        );

        // Assert
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("mise test version");
        result.StandardError.Should().NotContain("download must not run");
    }

    [Fact(SkipExceptions = [typeof(ShellUnavailableException)])]
    public async Task ANewMiseIsAvailableToTheNextRun()
    {
        // Arrange: the synthetic installer writes a runnable mise under HOME.

        // Act
        var result = await RunBootstrapAsync(
            """
            command() { return 1; }
            curl() {
                while [ "$1" != '-o' ]; do shift; done
                cat > "$2" <<'INSTALLER'
            mkdir -p "$HOME/.local/bin"
            printf '%s\n' '#!/bin/sh' 'echo "mise $*"' > "$HOME/.local/bin/mise"
            chmod +x "$HOME/.local/bin/mise"
            INSTALLER
            }
            """,
            "sh -c 'GITHUB_TOKEN= mise use -g -y dotnet@10.0.403'"
        );

        // Assert
        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardOutput.Should().Contain("mise --version");
        result.StandardOutput.Should().Contain("mise use -g -y dotnet@10.0.403");
    }

    [Fact]
    public void TheVersionsOfOneToolAreInstalledInOneStep()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - dotnet@9.0.303
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("mise use -g -y dotnet@10.0.403 dotnet@9.0.303");
        TestText.CountOccurrences(dockerfile, "mise use -g -y").Should().Be(1);
    }

    [Fact]
    public void TheToolsAreInstalledInTheOrderOfTheWorkload()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
              - npm:@opencode/cli@2.3.0
              - dotnet@10.0.403
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        var steps = Regex
            .Matches(dockerfile, @"(?m)mise use -g -y (\S+)$")
            .Select(match => match.Groups[1].Value);
        steps.Should().Equal("nodejs@24.4.3", "npm:@opencode/cli@2.3.0", "dotnet@10.0.403");
    }

    [Fact]
    public void TheScriptsRunAtTheEndOfTheBuild()
    {
        // Arrange
        _root.File("enable-fzf.sh", "echo fzf");
        var workload = WriteWorkload(
            """
            scripts:
              - enable-fzf.sh
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain("COPY scripts/ /tmp/abox-scripts/");
        dockerfile.Should().Contain("RUN bash /tmp/abox-scripts/enable-fzf.sh");
        File.Exists(Path.Combine(_root.Path, "kit", "scripts", "enable-fzf.sh")).Should().BeTrue();
    }

    [Theory]
    [InlineData("mti-ca-dotnet")]
    [InlineData("[{&!a #b é雪'😀}]")]
    public void GeneratedFilesUseLinuxLineEndings(string name)
    {
        // Arrange
        _root.File("setup.sh", "#!/bin/bash\r\necho setup\r\n");
        var workload = WriteWorkload(
            "apt: [git]\nmise: [nodejs@24.4.3]\nscripts: [setup.sh]\n",
            name
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        foreach (var file in new[] { "workload.dockerfile", "workload.yaml", "scripts/setup.sh" })
        {
            var content = File.ReadAllText(Path.Combine(_root.Path, "kit", file));
            content.Should().NotContain("\r").And.NotContain("\\uD83D");
        }
        var descriptor = new YamlDotNetParser().ReadFile(
            Path.Combine(_root.Path, "kit", "workload.yaml")
        );
        descriptor.GetString("displayName").Should().Be(name);
        descriptor
            .GetString("description")
            .Should()
            .Be($"The base image built from {name}.workload.");
    }

    [Fact]
    public void ScriptCopyOnlyReplacesCrLfAndPreservesEveryOtherByte()
    {
        // Arrange
        var source = _root.File("setup.sh", "");
        byte[] content =
        [
            0x23,
            0x20,
            0xe9,
            0x0d,
            0x0a,
            0x61,
            0x0d,
            0x62,
            0x85,
            0x0c,
            0xe2,
            0x80,
            0xa8,
            0xe2,
            0x80,
            0xa9,
            0x0d,
        ];
        File.WriteAllBytes(source, content);

        // Act
        _generator.Generate(
            WriteWorkload("scripts: [setup.sh]\n"),
            Path.Combine(_root.Path, "kit")
        );

        // Assert
        File.ReadAllBytes(Path.Combine(_root.Path, "kit", "scripts", "setup.sh"))
            .Should()
            .Equal([
                0x23,
                0x20,
                0xe9,
                0x0a,
                0x61,
                0x0d,
                0x62,
                0x85,
                0x0c,
                0xe2,
                0x80,
                0xa8,
                0xe2,
                0x80,
                0xa9,
                0x0d,
            ]);
        File.ReadAllBytes(source).Should().Equal(content);
    }

    [Theory]
    [InlineData("setup.sh")]
    [InlineData("SETUP.SH")]
    public void ScriptsWithTheSameFileNameCannotOverwriteEachOther(string secondName)
    {
        // Arrange
        _root.File("first/setup.sh", "echo first\n");
        _root.File($"second/{secondName}", "echo second\n");
        var workload = WriteWorkload($"scripts: [first/setup.sh, second/{secondName}]\n");

        // Act
        var action = () => _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        action
            .Should()
            .Throw<AboxException>()
            .WithMessage($"*first/setup.sh*second/{secondName}*");
    }

    [Fact]
    public void TheGitHubTokenIsASecretAndNotAnArgument()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            mise:
              - nodejs@24.4.3
            """
        );

        // Act
        _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        dockerfile.Should().Contain($"--mount=type=secret,id={WorkloadBuildSecrets.GitHubToken}");
        dockerfile.Should().NotContain("--build-arg");
    }

    [Fact]
    public void AScriptThatIsNotThereIsAnError()
    {
        // Arrange
        var workload = WriteWorkload(
            """
            scripts:
              - missing.sh
            """
        );

        // Act
        var action = () => _generator.Generate(workload, Path.Combine(_root.Path, "kit"));

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*missing.sh*");
    }

    private WorkloadFile WriteWorkload(string content, string name = "mti-ca-dotnet")
    {
        var path = _root.File(name + ".workload", content);
        return WorkloadFile.Load(path, new YamlDotNetParser());
    }

    private async Task<ProcessResult> RunBootstrapAsync(string commands, string afterBootstrap = "")
    {
        var shell = TestShell.Require(
            "Git for Windows sh is needed to execute Docker RUN commands."
        );

        _generator.Generate(
            WriteWorkload("mise: [dotnet@10.0.403]\n"),
            Path.Combine(_root.Path, "kit")
        );
        var dockerfile = File.ReadAllText(Path.Combine(_root.Path, "kit", "workload.dockerfile"));
        var bootstrap = Regex.Match(dockerfile, @"(?m)^RUN set -eu;(?:[^\n]*\\\r?\n)*[^\n]*").Value;
        bootstrap.Should().StartWith("RUN ");
        var script =
            "HOME=\"$PWD\"; export HOME; PATH=\"$HOME/.local/bin:/usr/bin:/bin\"; export PATH\n"
            + commands
            + "\n"
            + Regex.Replace(bootstrap[4..], @"\\\r?\n", "\n")
            + "\n"
            + afterBootstrap;

        return await new ProcessRunner().RunAsync(
            new ProcessRequest(shell, ["-c", script], WorkingDirectory: _root.Path),
            CancellationToken.None
        );
    }
}
