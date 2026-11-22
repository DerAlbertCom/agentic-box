using Abox.Features.Shared.KitInspection;

namespace Abox.Infrastructure.Sbx;

/// <summary>Kit inspection through <c>sbx kit inspect</c>, which reads declarations and does not build recipe-based kits.</summary>
internal sealed class SbxKitInspector(SbxInvoker invoker) : IKitInspector
{
    public async Task<KitInspectionOutcome> InspectAsync(
        string kitDirectory,
        IReadOnlyDictionary<string, string> arguments,
        CancellationToken cancellationToken
    )
    {
        var command = new List<string> { "kit", "inspect", kitDirectory };
        foreach (var (name, value) in arguments)
        {
            command.AddRange(["--kit-arg", $"{name}={value}"]);
        }

        var result = await invoker.RunAsync(invoker.Call(command), cancellationToken);
        return new KitInspectionOutcome(result.Succeeded, result.Output);
    }
}
