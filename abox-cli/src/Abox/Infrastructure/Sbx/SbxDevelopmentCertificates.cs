using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Abox.Features.Shared.Https;

namespace Abox.Infrastructure.Sbx;

/// <summary>
/// Protected development HTTPS through <c>sbx exec</c>: owns the embedded receiver, trust and
/// exec scripts and maps their exit codes to the domain outcomes of each operation.
/// </summary>
internal sealed class SbxDevelopmentCertificates(IProcessRunner runner)
    : ISandboxDevelopmentCertificates
{
    private readonly SbxInvoker _invoker = new(runner);
    private static readonly Lazy<string> DevelopmentCertificateExecBootstrap = new(() =>
        ReadDevelopmentCertificateResource("Abox.DevelopmentCertificateExec")
    );
    private static readonly Lazy<string> DevelopmentCertificateReceiver = new(
        ReadDevelopmentCertificateReceiver
    );

    internal static void AppendExecCommand(List<string> arguments, IReadOnlyList<string> command)
    {
        arguments.AddRange([
            "sh",
            "-c",
            DevelopmentCertificateExecBootstrap.Value,
            "abox-https",
            Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(DevelopmentCertificateReceiver.Value)
                )
            ),
        ]);
        arguments.AddRange(command);
    }

    // Exit codes shared with the embedded receiver; these are not sbx exit conventions.
    private enum DevelopmentCertificateOutcome
    {
        StaleParents = 42,
        ProcessEnvironmentsUnavailable = 43,
        NodeExtraCaIncomplete = 44,
        RuntimeBusy = 75,
        InvalidConfiguration = 78,
    }

    public async Task<SandboxCertificateActivation> ActivateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string publicCertificate;
        try
        {
            publicCertificate = await EstablishDevelopmentCertificateTrustAsync(
                sandbox,
                pfx,
                password,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            return new(SandboxCertificateActivationStatus.TrustCanceled);
        }
        catch (Exception)
        {
            return new(SandboxCertificateActivationStatus.TrustFailed);
        }
        return await TransferDevelopmentCertificateAsync(
            sandbox,
            pfx,
            password,
            "activate",
            cancellationToken,
            publicCertificate
        );
    }

    private async Task<string> EstablishDevelopmentCertificateTrustAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    )
    {
        using var certificate = X509CertificateLoader.LoadPkcs12(
            pfx.Span,
            password,
            X509KeyStorageFlags.EphemeralKeySet
        );
        var publicCertificate = certificate.ExportCertificatePem();
        var uid = await GetDevelopmentCertificateUserAsync(sandbox, cancellationToken);
        await TrustPublicDevelopmentCertificateAsync(
            sandbox,
            uid,
            publicCertificate,
            cancellationToken
        );
        return publicCertificate;
    }

    private async Task<string> GetDevelopmentCertificateUserAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var user = await _invoker.RunAsync(
            new ProcessRequest(
                _invoker.ExecutableName,
                ["exec", sandbox, "id", "-u"],
                StandardInput: "",
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            cancellationToken
        );
        var uid = user.StandardOutput.Trim();
        if (
            !user.Succeeded
            || !System.Text.RegularExpressions.Regex.IsMatch(uid, "^[1-9][0-9]{0,9}$")
        )
            throw new InvalidOperationException();
        return uid;
    }

    private async Task TrustPublicDevelopmentCertificateAsync(
        string sandbox,
        string uid,
        string publicCertificate,
        CancellationToken cancellationToken
    )
    {
        var trust = await _invoker.RunAsync(
            new ProcessRequest(
                _invoker.ExecutableName,
                [
                    "exec",
                    "-i",
                    "-u",
                    "root",
                    sandbox,
                    "python3",
                    "-I",
                    "-c",
                    ReadDevelopmentCertificateResource("Abox.DevelopmentCertificateTrust"),
                    uid,
                ],
                StandardInput: publicCertificate,
                OutputPolicy: ProcessOutputPolicy.Sensitive
            ),
            cancellationToken
        );
        if (!trust.Succeeded)
            throw new InvalidOperationException();
    }

    public async Task<SandboxCertificatePreparation> PrepareAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await _invoker.RunAsync(
                DevelopmentCertificateReceiverRequest(
                    sandbox,
                    "prepare",
                    Guid.NewGuid().ToString("N"),
                    null
                ) with
                {
                    OutputPolicy = ProcessOutputPolicy.CapturedSensitive,
                },
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            return new(SandboxCertificateStatus.Canceled);
        }
        catch (Exception)
        {
            return new(SandboxCertificateStatus.Unavailable);
        }
        if (result.ExitCode == (int)DevelopmentCertificateOutcome.RuntimeBusy)
            return new(SandboxCertificateStatus.Busy);
        var password = result.StandardOutput.Trim();
        if (
            !result.Succeeded
            || !System.Text.RegularExpressions.Regex.IsMatch(password, "^[0-9a-f]{64}$")
        )
            return new(SandboxCertificateStatus.Failed);
        return new(SandboxCertificateStatus.Succeeded, password);
    }

    public async Task<SandboxCertificateStatus> VerifyActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await RunDevelopmentCertificateReceiverAsync(
                sandbox,
                "status",
                Guid.NewGuid().ToString("N"),
                null,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            return SandboxCertificateStatus.Canceled;
        }
        catch (Exception)
        {
            return SandboxCertificateStatus.Unavailable;
        }
        return result.ExitCode switch
        {
            (int)DevelopmentCertificateOutcome.StaleParents =>
                SandboxCertificateStatus.StaleParents,
            (int)DevelopmentCertificateOutcome.ProcessEnvironmentsUnavailable =>
                SandboxCertificateStatus.ProcessEnvironmentsUnavailable,
            (int)DevelopmentCertificateOutcome.NodeExtraCaIncomplete =>
                SandboxCertificateStatus.NodeExtraCaIncomplete,
            (int)DevelopmentCertificateOutcome.RuntimeBusy => SandboxCertificateStatus.Busy,
            _ when !result.Succeeded => SandboxCertificateStatus.Failed,
            _ => SandboxCertificateStatus.Succeeded,
        };
    }

    public async Task<SandboxCertificateStatus> RefreshBootstrapAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        ProcessResult result;
        try
        {
            result = await RunDevelopmentCertificateReceiverAsync(
                sandbox,
                "refresh-bootstrap",
                Guid.NewGuid().ToString("N"),
                null,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            return SandboxCertificateStatus.Canceled;
        }
        catch (Exception)
        {
            return SandboxCertificateStatus.Unavailable;
        }
        if (result.ExitCode == (int)DevelopmentCertificateOutcome.RuntimeBusy)
            return SandboxCertificateStatus.Busy;
        return result.Succeeded
            ? SandboxCertificateStatus.Succeeded
            : SandboxCertificateStatus.Failed;
    }

    private async Task<SandboxCertificateActivation> TransferDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        string mode,
        CancellationToken cancellationToken,
        string? publicCertificate = null
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = DevelopmentCertificateFrame.Create(pfx, password, publicCertificate);
        if (frame is null)
            return new(SandboxCertificateActivationStatus.InvalidPayload);
        var transfer = Guid.NewGuid().ToString("N");
        SandboxCertificateActivationStatus failure;
        try
        {
            var result = await RunDevelopmentCertificateReceiverAsync(
                sandbox,
                mode,
                transfer,
                frame,
                cancellationToken
            );
            if (result.Succeeded)
                return new(SandboxCertificateActivationStatus.Activated);
            failure =
                result.ExitCode == (int)DevelopmentCertificateOutcome.RuntimeBusy
                    ? SandboxCertificateActivationStatus.TransferBusy
                    : SandboxCertificateActivationStatus.TransferFailed;
        }
        catch (OperationCanceledException)
        {
            failure = SandboxCertificateActivationStatus.TransferCanceled;
        }
        catch (Exception)
        {
            failure = SandboxCertificateActivationStatus.TransferFailed;
        }
        var cleaned = await TryCleanupDevelopmentCertificateAsync(sandbox, transfer);
        return new(failure, cleaned);
    }

    private async Task<bool> TryCleanupDevelopmentCertificateAsync(string sandbox, string transfer)
    {
        // Killing local sbx does not prove remote cleanup. Use a fresh, bounded token,
        // not the canceled transfer token, and never replace the primary failure.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return (
                await RunDevelopmentCertificateReceiverAsync(
                    sandbox,
                    "cleanup",
                    transfer,
                    null,
                    timeout.Token
                )
            ).Succeeded;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Task<ProcessResult> RunDevelopmentCertificateReceiverAsync(
        string sandbox,
        string mode,
        string transfer,
        string? frame,
        CancellationToken cancellationToken
    ) =>
        _invoker.RunAsync(
            DevelopmentCertificateReceiverRequest(sandbox, mode, transfer, frame),
            cancellationToken
        );

    private ProcessRequest DevelopmentCertificateReceiverRequest(
        string sandbox,
        string mode,
        string transfer,
        string? frame
    )
    {
        List<string> arguments = ["exec"];
        if (frame is not null)
            arguments.Add("-i");
        arguments.AddRange([
            sandbox,
            "python3",
            "-I",
            "-c",
            DevelopmentCertificateReceiverProgram,
            mode,
            transfer,
        ]);
        return new ProcessRequest(
            _invoker.ExecutableName,
            arguments,
            StandardInput: frame ?? string.Empty,
            OutputPolicy: ProcessOutputPolicy.Sensitive
        );
    }

    private static string DevelopmentCertificateReceiverProgram =>
        "SOURCE = "
        + JsonSerializer.Serialize(DevelopmentCertificateReceiver.Value)
        + "\nexec(compile(SOURCE, '<abox-https>', 'exec'))";

    private static string ReadDevelopmentCertificateReceiver() =>
        ReadDevelopmentCertificateResource("Abox.DevelopmentCertificateReceiver");

    private static string ReadDevelopmentCertificateResource(string name)
    {
        using var stream =
            typeof(SbxInvoker).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing certificate receiver resource.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
