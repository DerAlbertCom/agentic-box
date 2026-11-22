namespace Abox.Features.Shared.CommandLine;

/// <summary>When a sandbox location loads the folder configuration there.</summary>
internal enum FolderConfigurationLoading
{
    /// <summary>While binding, so an invalid folder configuration fails before the handler.</summary>
    Now,

    /// <summary>When the handler first asks for it.</summary>
    OnFirstUse,

    /// <summary>Never; the location has no folder configuration.</summary>
    Never,
}
