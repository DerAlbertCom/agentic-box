using Abox.Features.Shared.Squads;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// The secrets the host keeps for every sandbox: lists, stores and removes them, and tells
/// how the user stores a custom secret from a host command abox never runs itself.
/// </summary>
public interface IHostSecretStore
{
    /// <summary>The secrets the host keeps.</summary>
    Task<HostSecrets> ListAsync(CancellationToken cancellationToken);

    /// <summary>Stores the value of a service the host store knows by name.</summary>
    Task<HostSecretChange> SetAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Stores a custom secret under its hosts, variable and placeholder. Without a value the
    /// store takes it from the declaration's host command and checks that it gives one.
    /// </summary>
    Task<HostSecretChange> SetCustomAsync(
        SecretDeclaration declaration,
        string? value,
        CancellationToken cancellationToken
    );

    /// <summary>Removes the stored secret with the identity.</summary>
    Task<HostSecretChange> RemoveAsync(
        SecretIdentity identity,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// What the user runs to store a custom secret from a host command themselves.
    /// </summary>
    /// <param name="declaration">The custom secret.</param>
    /// <param name="hostCommand">The host command, or a stand-in for it.</param>
    string ManualCustomSecretInstruction(SecretDeclaration declaration, string hostCommand);
}

/// <summary>How a change of the host's secrets went.</summary>
/// <param name="Succeeded">Whether the host store made the change.</param>
/// <param name="Message">What the host store said, so a failure can be shown to the user.</param>
public sealed record HostSecretChange(bool Succeeded, string Message)
{
    /// <summary>The change was made.</summary>
    public static HostSecretChange Done { get; } = new(true, string.Empty);
}
