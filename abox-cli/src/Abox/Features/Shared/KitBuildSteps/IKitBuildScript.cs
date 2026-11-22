namespace Abox.Features.Shared.KitBuildSteps;

/// <summary>Runs the <c>build</c> file of a local kit (ADR 0001).</summary>
public interface IKitBuildScript
{
    /// <summary>
    /// Runs the build file into the staged copy, from the kit's own folder. Throws an
    /// <see cref="AboxException"/> naming the kit and showing the build's output when it fails.
    /// </summary>
    Task RunAsync(KitBuildScriptRun run, CancellationToken cancellationToken);
}

/// <summary>One run of a kit's build file.</summary>
/// <param name="KitName">The kit, as named in failures.</param>
/// <param name="BuildFile">The executable build file.</param>
/// <param name="StagedDirectory">The staged copy of the kit that the build writes into.</param>
/// <param name="KitDirectory">The kit's folder in the repository, where the build runs.</param>
public sealed record KitBuildScriptRun(
    string KitName,
    string BuildFile,
    string StagedDirectory,
    string KitDirectory
);
