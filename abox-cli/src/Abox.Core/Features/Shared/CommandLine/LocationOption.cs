using System.CommandLine;

namespace Abox.Features.Shared.CommandLine;

/// <summary>The <c>--location</c> option of every command that works on a sandbox location.</summary>
internal sealed class LocationOption : Option<string>
{
    public LocationOption()
        : base("--location")
    {
        Description =
            "The folder of the sandbox. Defaults to defaultLocation in the user configuration, then the current folder.";
    }
}
