namespace Abox.Features.Shared.Registry;

/// <summary>The host's sign-in to the abox registry, kept by the image tooling.</summary>
public interface IRegistryLogin
{
    /// <summary>Whether the stored sign-in is accepted by the registry. Never asks the user.</summary>
    Task<bool> IsSignedInAsync(CancellationToken cancellationToken);

    /// <summary>Lets the user sign in on the terminal. Returns whether the sign-in succeeded.</summary>
    Task<bool> SignInAsync(CancellationToken cancellationToken);
}
