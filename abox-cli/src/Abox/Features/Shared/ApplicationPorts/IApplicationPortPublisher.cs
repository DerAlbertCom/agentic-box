namespace Abox.Features.Shared.ApplicationPorts;

/// <summary>Publishes application listeners of a sandbox on host IPv4 loopback.</summary>
public interface IApplicationPortPublisher
{
    /// <summary>Starts the sandbox if needed and ensures loopback TCP app mappings without removing others.</summary>
    Task PublishAsync(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> mappings,
        CancellationToken cancellationToken
    );
}
