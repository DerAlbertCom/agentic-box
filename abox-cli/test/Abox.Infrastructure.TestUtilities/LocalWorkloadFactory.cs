using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Preflight;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

/// <summary>A synthetic Docker for local component images and registry digests.</summary>
internal static class LocalWorkloadFactory
{
    /// <summary>The content ID the synthetic image store reports for every image.</summary>
    public static readonly string ImageId = "sha256:" + new string('a', 64);

    public static IProcessRunner Runner()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Result(call.Arg<ProcessRequest>()));
        return runner;
    }

    /// <summary>Host programs where Docker is installed.</summary>
    public static IHostPrograms Programs()
    {
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("docker").Returns(true);
        return programs;
    }

    /// <summary>The digest the synthetic Docker reports for a reference: built, pushed or resolved.</summary>
    public static string Digest(string reference) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reference)));

    public static ProcessResult Result(ProcessRequest request)
    {
        var arguments = request.Arguments.ToList();
        if (arguments.Contains("--metadata-file"))
        {
            File.WriteAllText(
                arguments[arguments.IndexOf("--metadata-file") + 1],
                JsonSerializer.Serialize(
                    new Dictionary<string, string>
                    {
                        ["containerimage.digest"] = Digest(
                            arguments[arguments.IndexOf("--tag") + 1]
                        ),
                    }
                )
            );
        }

        return new(
            0,
            arguments.Contains("{{.Id}}") ? ImageId
                : arguments.Contains("{{json .Manifest}}")
                    ? JsonSerializer.Serialize(new { digest = Digest(arguments[^1]) })
                : arguments.Contains("ls") ? "{\"Name\":\"test-context\",\"Driver\":\"docker\"}"
                : arguments.SequenceEqual(new[] { "context", "show" }) ? "test-context"
                : "",
            ""
        );
    }

    public static LocalWorkloadImages Images(IProcessRunner? runner = null) =>
        Images(Programs(), runner ?? Runner());

    /// <summary>Local workload images backed by the Docker adapter and the given fakes.</summary>
    public static LocalWorkloadImages Images(IHostPrograms programs, IProcessRunner runner) =>
        new(new DockerComponentImages(programs, runner));

    /// <summary>A prebuilt workload image with its thin runtime kit, as a build records it.</summary>
    public static LocalWorkloadImage Artifact(TempDirectory root)
    {
        var source = root.File(
            "source/workload.yaml",
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
        var runtime = Path.Combine(root.Path, "runtime", "workload");
        const string reference = "rwest.jfrog.io/repo/components/workload:1";
        LocalWorkloadImages.WriteRuntimeKit(Path.GetDirectoryName(source)!, runtime, reference);
        return new LocalWorkloadImage
        {
            Reference = reference,
            ImageId = ImageId,
            ImageStore = "test-context",
            RuntimeKitDirectory = runtime,
            RuntimeKitHash = BuildSources.DirectoryHash(runtime),
        };
    }
}
