using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Simulates a CLI export failure without store access or external processes.</summary>
internal sealed class SyntheticCertificateExport : IDisposable
{
    private readonly string _condition;
    private readonly SyntheticDevelopmentCertificate _identity;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly byte[] _bytes;
    private readonly JsonArray _before;
    private readonly JsonArray _after;
    private int _reads;

    internal SyntheticCertificateExport(string condition, string password)
    {
        _condition = condition;
        _identity = new SyntheticDevelopmentCertificate(
            format: condition == "bad-format" ? 4 : 6,
            dnsName: condition == "bad-san" ? "not-localhost" : "localhost",
            before: condition == "future-pfx" ? DateTimeOffset.UtcNow.AddDays(1) : null,
            after: condition == "expired-pfx" ? DateTimeOffset.UtcNow.AddDays(-1) : null
        );
        _bytes =
            condition == "no-key"
                ? _identity.ExportPublicOnly(password)
                : _identity.Export(password);
        _before = EligibleReport();
        _after = FreshReport();
        Runner = Substitute.For<IProcessRunner>();
        Runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Respond(call.Arg<ProcessRequest>()));
    }

    internal IProcessRunner Runner { get; }
    internal CancellationToken Token => _cancellation.Token;

    private JsonArray EligibleReport()
    {
        var metadata = _identity.Metadata();
        metadata[0]!["Version"] = 6;
        if (_condition == "wrong-identity")
            metadata[0]!["Thumbprint"] = new string('A', 40);
        if (_condition == "future-pfx")
            metadata[0]!["ValidityNotBefore"] = DateTime.UtcNow.AddDays(-1);
        if (_condition == "expired-pfx")
            metadata[0]!["ValidityNotAfter"] = DateTime.UtcNow.AddDays(1);
        return metadata;
    }

    private JsonArray FreshReport()
    {
        var metadata = _before.DeepClone().AsArray();
        switch (_condition)
        {
            case "post-untrusted":
                metadata[0]!["TrustLevel"] = "None";
                break;
            case "post-empty":
                metadata.Clear();
                break;
            case "post-multiple":
                metadata.Add(metadata[0]!.DeepClone());
                break;
            case "post-renewed":
                metadata[0]!["Thumbprint"] = new string('B', 40);
                break;
            case "post-version":
                metadata[0]!["Version"] = 7;
                break;
            case "post-valid-from":
                metadata[0]!["ValidityNotBefore"] = _identity
                    .Certificate.NotBefore.ToUniversalTime()
                    .AddDays(-1);
                break;
            case "post-valid-until":
                metadata[0]!["ValidityNotAfter"] = _identity
                    .Certificate.NotAfter.ToUniversalTime()
                    .AddDays(1);
                break;
        }
        return metadata;
    }

    private Task<ProcessResult> Respond(ProcessRequest child)
    {
        if (!child.Arguments.Contains("--export-path"))
        {
            if (_condition == "post-cancel" && _reads != 0)
                return Cancel();
            return Task.FromResult(
                new ProcessResult(
                    0,
                    (_reads++ == 0 ? _before : _after).ToJsonString(),
                    "secret metadata"
                )
            );
        }
        var path = child.Arguments[Array.IndexOf(child.Arguments.ToArray(), "--export-path") + 1];
        WriteExport(path);
        if (_condition == "cancel")
            return Cancel();
        if (_condition == "throw")
            return Task.FromException<ProcessResult>(new IOException("secret export"));
        return Task.FromResult(
            new ProcessResult(
                _condition == "partial-export" ? 9 : 0,
                "secret export",
                "secret password"
            )
        );
    }

    private void WriteExport(string path)
    {
        if (_condition == "missing-pfx")
            return;
        File.WriteAllBytes(
            path,
            _condition switch
            {
                "empty-pfx" => [],
                "oversized-pfx" => new byte[1024 * 1024 + 1],
                "invalid-pfx" => [1, 2, 3],
                _ => _bytes,
            }
        );
    }

    private Task<ProcessResult> Cancel()
    {
        _cancellation.Cancel();
        return Task.FromException<ProcessResult>(
            new OperationCanceledException("secret cancellation", _cancellation.Token)
        );
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_bytes);
        _identity.Dispose();
        _cancellation.Dispose();
    }
}
