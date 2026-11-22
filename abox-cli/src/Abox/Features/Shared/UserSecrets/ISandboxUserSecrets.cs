namespace Abox.Features.Shared.UserSecrets;

/// <summary>The sandbox side of a copy: its authorized host ports and its user-secret stores.</summary>
public interface ISandboxUserSecrets
{
    /// <summary>Explicit sandbox-declared TCP host ports currently authorized for this sandbox.</summary>
    Task<IReadOnlyList<int>> GetUserSecretsHostPortsAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>Checks effective application-user paths for all stores without creating files.</summary>
    Task ValidateUserSecretsDestinationAsync(
        string sandbox,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken
    );

    /// <summary>Replaces one store atomically; the payload never reaches arguments or diagnostics.</summary>
    Task WriteUserSecretsAsync(
        string sandbox,
        string id,
        string json,
        CancellationToken cancellationToken
    );
}
