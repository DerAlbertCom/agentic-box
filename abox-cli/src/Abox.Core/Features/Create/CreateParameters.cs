using Abox.Features.Shared.CommandLine;

namespace Abox.Features.Create;

/// <summary>
/// The parameters of <c>abox create</c>. <see cref="SourceRoot"/> is the full path where
/// <c>--local</c> starts searching for team sources: <c>--root</c>, otherwise the location.
/// </summary>
internal sealed record CreateParameters(
    SandboxLocation Location,
    string Team,
    string? Squad,
    string Tag,
    string SourceRoot,
    bool Local,
    bool AssumeYes
);
