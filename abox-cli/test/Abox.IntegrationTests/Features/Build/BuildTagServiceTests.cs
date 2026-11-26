using Abox.Features.Build;
using Abox.Features.Shared.Builds;
using Abox.Features.Shared.Common;
using Abox.Features.Shared.Configuration;
using Abox.Features.Shared.Preflight;
using Abox.Features.Shared.Squads;
using Abox.Features.Shared.Yaml;
using Abox.Infrastructure;
using Abox.Infrastructure.Docker;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildTagServiceTests
{
    [Fact]
    public async Task DirectBuildRejectsAnInvalidTagEvenWhenNoSquadsAreSelected()
    {
        // Arrange
        using var root = new TempDirectory();
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var runner = Substitute.For<IProcessRunner>();
        var programs = Substitute.For<IHostPrograms>();
        var console = new TestConsole();
        var service = TestServiceGraphs.Build(
            new SquadResolver(new AboxConfig(), new YamlDotNetParser()),
            new BuildStore(paths),
            runner,
            console,
            programs: programs
        );
        var request = new BuildRequest([], root.Path, "../outside");

        // Act
        var build = () => service.BuildAsync(request, CancellationToken.None);

        // Assert
        (await build.Should().ThrowAsync<AboxException>()).WithMessage(
            "*not a valid registry tag*"
        );
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }
}
