using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.Tests;

internal static class PublishTestProcess
{
    public static ProcessResult Run(ProcessRequest request)
    {
        var arguments = request.Arguments.ToList();
        if (arguments.Contains("--raw"))
        {
            var reference = arguments.Last();
            var kind =
                reference.StartsWith("ghcr.io/")
                || reference.Contains("/components/") && !reference.Contains("/components/workload")
                    ? "mixin"
                    : "workload";
            return new ProcessResult(0, Manifest(kind), string.Empty);
        }

        return LocalWorkloadFactory.Result(request);
    }

    public static string Manifest(string kind, object? args = null) =>
        JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                mediaType = "application/vnd.oci.image.manifest.v1+json",
                annotations = new Dictionary<string, string>
                {
                    ["vnd.docker.sandbox.kit.descriptor"] = JsonSerializer.Serialize(
                        new
                        {
                            schemaVersion = "3",
                            kind,
                            args = args
                                ?? new
                                {
                                    message = new { env = "MESSAGE" },
                                    url = new { env = "URL" },
                                },
                        }
                    ),
                },
            }
        );
}
