using Abox.Features.Shared.Sandboxes;

namespace Abox.Infrastructure.Sbx;

/// <summary>The host's MCP servers through <c>sbx mcp ls</c> and <c>sbx mcp add</c>.</summary>
public sealed class SbxMcpServerRegistry(SbxInvoker invoker) : IMcpServerRegistry
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken)
    {
        var servers = await invoker.RunJsonAsync<List<SbxMcpServer>>(
            ["mcp", "ls", "--json"],
            "sbx mcp ls",
            cancellationToken
        );
        return servers
            .Select(server => server.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    public async Task<McpServerRegistration> AddAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    )
    {
        var outcome = await invoker.RunOutcomeAsync(
            invoker.Call(["mcp", "add", name, "--url", url]),
            cancellationToken
        );
        return new McpServerRegistration(outcome.Succeeded, outcome.Output);
    }
}
