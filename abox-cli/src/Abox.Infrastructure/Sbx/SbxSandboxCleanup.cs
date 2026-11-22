using System.Text.RegularExpressions;
using Abox.Features.Shared.SandboxCleanup;

namespace Abox.Infrastructure.Sbx;

/// <summary>Sandbox cleanup through <c>sbx ls</c>, <c>sbx stop</c> and <c>sbx template</c>.</summary>
public sealed partial class SbxSandboxCleanup(SbxInvoker invoker) : ISandboxCleanup
{
    /// <summary>The repository sbx keeps the assembled kit sets in.</summary>
    private const string AssembledRepository = "sandbox-kit-assembled";

    /// <summary>The repository sbx keeps the built kits in.</summary>
    private const string KitSourceRepository = "sbx-kit-src";

    /// <summary>The suffix of the alias tag a build gets for its snapshot.</summary>
    private const string TemplateSuffix = "-template";

    [GeneratedRegex(@"-kit-\d+$")]
    private static partial Regex KitIndexSuffix();

    public async Task<IReadOnlyList<SandboxSummary>> ListSandboxesAsync(
        CancellationToken cancellationToken
    )
    {
        var sandboxes = await invoker.RunJsonAsync<List<SbxSandbox>>(
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
        var result = await invoker.RunAsync(invoker.Call(["stop", name]), cancellationToken);
        return result.Succeeded;
    }

    public async Task<IReadOnlyList<TemplateImage>> ListTemplatesAsync(
        CancellationToken cancellationToken
    )
    {
        var templates = await invoker.RunJsonAsync<List<SbxTemplate>>(
            ["template", "ls", "--json"],
            "sbx template ls",
            cancellationToken
        );
        return templates
            .Where(template => !string.IsNullOrEmpty(template.Tag))
            .Select(template => ImageOf(template.Repository ?? string.Empty, template))
            .ToList();
    }

    private static TemplateImage ImageOf(string repository, SbxTemplate template)
    {
        var tag = template.Tag!;
        var (kind, set) =
            IsRepository(repository, AssembledRepository) ? (TemplateKind.AssembledSet, SetOf(tag))
            : IsRepository(repository, KitSourceRepository) ? (TemplateKind.KitSource, string.Empty)
            : (TemplateKind.Unrelated, string.Empty);
        return new TemplateImage(
            $"{repository}:{tag}",
            kind,
            set,
            template.Id ?? string.Empty,
            template.CreatedAt
        );
    }

    private static bool IsRepository(string repository, string name) =>
        repository.Equals(name, StringComparison.Ordinal)
        || repository.EndsWith("/" + name, StringComparison.Ordinal);

    /// <summary>The set a tag of the assembled repository belongs to.</summary>
    private static string SetOf(string tag)
    {
        var kit = KitIndexSuffix().Match(tag);
        if (kit.Success)
        {
            return tag[..kit.Index];
        }

        return tag.EndsWith(TemplateSuffix, StringComparison.Ordinal)
            ? tag[..^TemplateSuffix.Length]
            : tag;
    }

    public async Task<TemplateRemoval> RemoveTemplateAsync(
        TemplateImage image,
        CancellationToken cancellationToken
    )
    {
        var result = await invoker.RunAsync(
            invoker.Call(["template", "rm", image.Reference, "--force"]),
            cancellationToken
        );
        return result.Succeeded ? TemplateRemoval.Done : new(false, result.Output.Trim());
    }
}
