using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;

namespace Abox.Features.Shared.Credentials;

/// <summary>
/// A credential the kits of a squad ask for with the <c>credential@1</c> capability.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
/// <param name="Kits">The kits that ask for it.</param>
/// <param name="Domains">The domains its API key is sent to; empty without an <c>apiKey</c> mechanism.</param>
/// <param name="Optional">Whether every kit that asks starts without it.</param>
/// <param name="Description">What the credential is for, from the first kit that describes it.</param>
internal sealed record CredentialRequest(
    string Service,
    IReadOnlyList<string> Kits,
    IReadOnlyList<string> Domains,
    bool Optional,
    string? Description = null
)
{
    /// <summary>
    /// The credentials the local kits of the squad ask for, one per service. Kits from a
    /// registry cannot be inspected and are left out.
    /// </summary>
    public static IReadOnlyList<CredentialRequest> Of(ResolvedSquad squad, IYamlParser parser) =>
        Merge(
            squad
                .Kits.Where(kit => kit.IsLocal)
                .Select(kit => (kit.Name, Descriptor: KitDescriptor.Load(kit.Directory!, parser)))
                .Where(kit => kit.Descriptor is not null)
                .Select(kit => (kit.Name, kit.Descriptor!))
        );

    /// <summary>
    /// The credentials the kits ask for, one per service: the kits that ask, the union of
    /// their domains, and optional only when every kit that asks is optional about it.
    /// </summary>
    public static IReadOnlyList<CredentialRequest> Merge(
        IEnumerable<(string Kit, KitDescriptor Descriptor)> kits
    )
    {
        var requests = new List<CredentialRequest>();

        foreach (var (kit, descriptor) in kits)
        {
            foreach (var service in descriptor.Services)
            {
                var index = requests.FindIndex(request => request.Service == service.Service);
                if (index < 0)
                {
                    requests.Add(
                        new CredentialRequest(
                            service.Service,
                            [kit],
                            service.Domains,
                            service.Optional,
                            service.Description
                        )
                    );
                    continue;
                }

                var known = requests[index];
                requests[index] = known with
                {
                    Kits = known.Kits.Contains(kit) ? known.Kits : [.. known.Kits, kit],
                    Domains = known.Domains.Union(service.Domains).ToList(),
                    Optional = known.Optional && service.Optional,
                    Description = known.Description ?? service.Description,
                };
            }
        }

        return requests;
    }
}
