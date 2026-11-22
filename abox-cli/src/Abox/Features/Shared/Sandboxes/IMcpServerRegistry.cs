namespace Abox.Features.Shared.Sandboxes;

/// <summary>The MCP servers registered on the host, which sandboxes can carry.</summary>
public interface IMcpServerRegistry
{
    /// <summary>The names of the registered MCP servers. Discovery errors throw.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Registers an MCP server on the host. A refusal is returned, not thrown.</summary>
    Task<McpServerRegistration> AddAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    );
}

/// <summary>Whether an MCP server was registered.</summary>
/// <param name="Registered">Whether the host registered the server.</param>
/// <param name="Output">What the runtime wrote, so a refusal can be shown.</param>
public sealed record McpServerRegistration(bool Registered, string Output);
