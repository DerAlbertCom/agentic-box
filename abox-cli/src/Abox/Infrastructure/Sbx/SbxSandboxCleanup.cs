using Abox.Features.Shared.SandboxCleanup;

namespace Abox.Infrastructure.Sbx;

/// <summary>Sandbox cleanup through <c>sbx ls</c>, <c>sbx stop</c> and <c>sbx template</c>.</summary>
public sealed class SbxSandboxCleanup(IProcessRunner runner) : ISandboxCleanup
{
    private readonly SbxInvoker _invoker = new(runner);

    public async Task<IReadOnlyList<SandboxSummary>> ListSandboxesAsync(
        CancellationToken cancellationToken
    )
    {
        var sandboxes = await _invoker.RunJsonAsync<List<SbxSandbox>>(
            ["ls", "--json"],
            "sbx ls",
            cancellationToken
        );
        return sandboxes
            .Where(sandbox => !string.IsNullOrEmpty(sandbox.Name))
            .Select(sandbox => new SandboxSummary(
                sandbox.Name!,
                string.Equals(sandbox.Status, "running", StringComparison.OrdinalIgnoreCase)
            ))
            .ToList();
    }

    public async Task<bool> StopSandboxAsync(string name, CancellationToken cancellationToken)
    {
        var result = await _invoker.RunAsync(
            new ProcessRequest(_invoker.ExecutableName, ["stop", name]),
            cancellationToken
        );
        return result.Succeeded;
    }

    public async Task<IReadOnlyList<TemplateImage>> ListTemplatesAsync(
        CancellationToken cancellationToken
    )
    {
        var templates = await _invoker.RunJsonAsync<List<SbxTemplate>>(
            ["template", "ls", "--json"],
            "sbx template ls",
            cancellationToken
        );
        return templates
            .Where(template => !string.IsNullOrEmpty(template.Tag))
            .Select(template => new TemplateImage(
                template.Repository ?? string.Empty,
                template.Tag!,
                template.Id ?? string.Empty,
                template.CreatedAt
            ))
            .ToList();
    }

    public async Task<TemplateRemoval> RemoveTemplateAsync(
        TemplateImage image,
        CancellationToken cancellationToken
    )
    {
        var result = await _invoker.RunAsync(
            new ProcessRequest(
                _invoker.ExecutableName,
                ["template", "rm", image.Reference, "--force"]
            ),
            cancellationToken
        );
        return result.Succeeded ? TemplateRemoval.Done : new(false, result.Output.Trim());
    }
}
