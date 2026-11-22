namespace Abox.Features.Shared.UserSecrets;

/// <summary>The host's .NET user-secret stores, by store ID.</summary>
public interface IHostUserSecretStores
{
    /// <summary>Every store ID on this host, in a stable order; none when the store root is missing.</summary>
    IReadOnlyList<string> StoreIds(CancellationToken cancellationToken);

    Task<HostUserSecretStore> ReadAsync(string id, CancellationToken cancellationToken);
}

public enum HostUserSecretStoreState
{
    Read,
    Missing,
    Unreadable,
    InvalidUtf8,
}

/// <summary>One host store's raw text when it was read; the text never appears in ToString.</summary>
public sealed class HostUserSecretStore
{
    private HostUserSecretStore(HostUserSecretStoreState state, string json)
    {
        State = state;
        Json = json;
    }

    public HostUserSecretStoreState State { get; }

    /// <summary>The store's text; empty unless <see cref="State"/> is Read.</summary>
    public string Json { get; }

    public static HostUserSecretStore Read(string json) => new(HostUserSecretStoreState.Read, json);

    public static HostUserSecretStore Missing { get; } = new(HostUserSecretStoreState.Missing, "");

    public static HostUserSecretStore Unreadable { get; } =
        new(HostUserSecretStoreState.Unreadable, "");

    public static HostUserSecretStore InvalidUtf8 { get; } =
        new(HostUserSecretStoreState.InvalidUtf8, "");

    public override string ToString() => $"Host user-secret store ({State}; values hidden)";
}
