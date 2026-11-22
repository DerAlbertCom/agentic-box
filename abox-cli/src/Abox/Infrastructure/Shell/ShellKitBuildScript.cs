using Abox.Features.Shared.Common;
using Abox.Features.Shared.KitBuildScripts;

namespace Abox.Infrastructure.Shell;

/// <summary>Runs a kit's build file as <c>build &lt;stagedKitDir&gt;</c> in the kit's folder.</summary>
internal sealed class ShellKitBuildScript(IProcessRunner runner) : IKitBuildScript
{
    public async Task RunAsync(KitBuildScriptRun run, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new ProcessRequest(
                run.BuildFile,
                [run.StagedDirectory],
                WorkingDirectory: run.KitDirectory
            ),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException(
                $"The build of kit '{run.KitName}' failed with exit code {result.ExitCode}:{Environment.NewLine}{(result.StandardOutput + result.StandardError).Trim()}"
            );
        }
    }
}
