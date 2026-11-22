namespace Abox.Features.Shared.Preflight;

/// <summary>The installed version of the sandbox runtime and the first one abox works with.</summary>
public sealed record RuntimeVersion(Version Installed, Version Minimum)
{
    public bool Supported => Installed >= Minimum;
}
