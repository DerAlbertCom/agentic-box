using System.Text;
using Abox.Features.Shared.CommandLine;
using Abox.Features.Shared.Squads;
using Abox.Infrastructure;
using Abox.Infrastructure.Sbx;
using NSubstitute;

namespace Abox.Tests;

[CollectionDefinition("Console input encoding", DisableParallelization = true)]
public sealed class ConsoleInputEncodingCollection;

[Collection("Console input encoding")]
public sealed class SbxSecretInputTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 19)]
    [InlineData(true, 19)]
    public async Task HostSecretInputIsExactUtf8DespiteALegacyConsoleCodePage(
        bool custom,
        int exitCode
    )
    {
        // Arrange
        using var root = new TempDirectory();
        const string value = "x-ü€漢🔒-y";
        byte[] expected =
        [
            0x78,
            0x2D,
            0xC3,
            0xBC,
            0xE2,
            0x82,
            0xAC,
            0xE6,
            0xBC,
            0xA2,
            0xF0,
            0x9F,
            0x94,
            0x92,
            0x2D,
            0x79,
        ];
        var console = new TestConsole();
        var processRunner = new ProcessRunner(
            new ProcessOutput(console, new Verbosity { Verbose = true })
        );
        var runner = Substitute.For<IProcessRunner>();
        ProcessRequest? emitted = null;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                emitted = call.Arg<ProcessRequest>();
                return processRunner.RunAsync(
                    emitted with
                    {
                        FileName = OperatingSystem.IsWindows() ? "powershell" : "sh",
                        Arguments = OperatingSystem.IsWindows()
                            ?
                            [
                                "-NoProfile",
                                "-NonInteractive",
                                "-Command",
                                "$file = [IO.File]::Create('received.bin'); [Console]::OpenStandardInput().CopyTo($file); $file.Dispose(); [Console]::Out.WriteLine('stored'); [Console]::Error.WriteLine('warning'); exit "
                                    + exitCode,
                            ]
                            :
                            [
                                "-c",
                                $"cat > received.bin; printf 'stored\\n'; printf 'warning\\n' >&2; exit {exitCode}",
                            ],
                        WorkingDirectory = root.Path,
                    },
                    call.Arg<CancellationToken>()
                );
            });
        var store = new SbxHostSecretStore(new SbxInvoker(runner));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var previousEncoding = Console.InputEncoding;
        try
        {
            Console.InputEncoding = CodePagesEncodingProvider.Instance.GetEncoding(850)!;

            // Act
            var change = custom
                ? await store.SetCustomAsync(
                    new SecretDeclaration(
                        "synthetic",
                        Hosts: ["example.test"],
                        EnvironmentVariable: "SYNTHETIC_TOKEN"
                    ),
                    value,
                    cancellation.Token
                )
                : await store.SetAsync("synthetic", value, cancellation.Token);

            // Assert
            File.ReadAllBytes(Path.Combine(root.Path, "received.bin")).Should().Equal(expected);
            change.Succeeded.Should().Be(exitCode == 0);
            // stdout and stderr arrive on separate pipes, so their order in the combined output varies.
            change
                .Message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Should()
                .BeEquivalentTo(["stored", "warning"]);
            console.AllText.Should().Contain("stored").And.Contain("warning").And.NotContain(value);
            emitted.Should().NotBeNull();
            emitted!.FileName.Should().Be("sbx");
            emitted
                .Arguments.Should()
                .Equal(
                    custom
                        ?
                        [
                            "secret",
                            "set-custom",
                            "--host",
                            "example.test",
                            "--env",
                            "SYNTHETIC_TOKEN",
                            "--value-stdin",
                        ]
                        : ["secret", "set", "synthetic", "--force"]
                );
            emitted.Environment.Should().BeNull();
            emitted.ToString().Should().NotContain(value);
            await runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token);
        }
        finally
        {
            Console.InputEncoding = previousEncoding;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostSecretSettersPreserveCancellation(bool custom)
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), cancellation.Token)
            .Returns(Task.FromCanceled<ProcessResult>(cancellation.Token));
        var store = new SbxHostSecretStore(new SbxInvoker(runner));

        // Act
        var action = () =>
            custom
                ? store.SetCustomAsync(
                    new SecretDeclaration(
                        "synthetic",
                        Hosts: ["example.test"],
                        EnvironmentVariable: "SYNTHETIC_TOKEN"
                    ),
                    "synthetic-ü€",
                    cancellation.Token
                )
                : store.SetAsync("synthetic", "synthetic-ü€", cancellation.Token);

        // Assert
        var exception = await action.Should().ThrowAsync<OperationCanceledException>();
        exception.Which.CancellationToken.Should().Be(cancellation.Token);
    }
}
