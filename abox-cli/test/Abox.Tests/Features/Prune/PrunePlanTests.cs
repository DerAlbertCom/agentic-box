using Abox.Features.Prune;
using Abox.Features.Shared.SandboxCleanup;

namespace Abox.Tests;

public sealed class PrunePlanTests
{
    [Fact]
    public void NormalPruneKeepsEveryNewestGroupIncludingOlderKitsAliasesAndSharedSources()
    {
        // Arrange
        var firstNewest = Assembled("new-a", "new-a", "a", 3);
        var olderKit = Assembled("new-a", "new-a-kit-0", "shared", 1);
        var alias = Assembled("new-a", "new-a-template", "shared", 1);
        var tiedNewest = Assembled("new-b", "new-b", "b", 3);
        var sharedSource = Source("shared-source", "shared", 1);
        var stale = Assembled("old", "old", "old", 2);
        var staleAlias = Assembled("old", "old-template", "shared", 1);
        var unused = Source("unused", "unused", 1);
        TemplateImage[] retained = [firstNewest, olderKit, alias, tiedNewest, sharedSource];

        // Act
        var plan = PruneService.Plan([.. retained, stale, staleAlias, unused]);

        // Assert
        plan.Keep.Should().BeEquivalentTo(retained);
        plan.Remove.Should().BeEquivalentTo<TemplateImage>([stale, staleAlias, unused]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UncertainAssembledSetsKeepTheirWholeGroupAndPotentialKitSources(
        bool full,
        bool missingAge
    )
    {
        // Arrange
        var uncertain = Assembled("uncertain", "uncertain-kit-0", "uncertain", 1);
        uncertain = missingAge ? uncertain with { CreatedAt = null } : uncertain with { Id = "" };
        var sibling = Assembled("uncertain", "uncertain-kit-1", "sibling", 1);
        var alias = Assembled("uncertain", "uncertain-template", "sibling", 1);
        var sharedSource = Source("shared", "sibling", 1);
        var potentialSource = Source("unknown-dependency", "potential", 1);
        var newest = Assembled("new", "new", "new", 3);
        var stale = Assembled("stale", "stale", "stale", 2);
        TemplateImage[] retained = [uncertain, sibling, alias, sharedSource, potentialSource];
        TemplateImage[] images = [.. retained, newest, stale];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Contain(retained);
        plan.Remove.Should().BeEquivalentTo<TemplateImage>(full ? [newest, stale] : [stale]);
    }

    [Theory]
    [InlineData(false, TemplateKind.AssembledSet, false)]
    [InlineData(false, TemplateKind.AssembledSet, true)]
    [InlineData(true, TemplateKind.AssembledSet, false)]
    [InlineData(true, TemplateKind.AssembledSet, true)]
    [InlineData(false, TemplateKind.KitSource, false)]
    [InlineData(false, TemplateKind.KitSource, true)]
    [InlineData(true, TemplateKind.KitSource, false)]
    [InlineData(true, TemplateKind.KitSource, true)]
    public void MissingAgeOrIdentityPreventsRemoval(bool full, TemplateKind kind, bool missingAge)
    {
        // Arrange
        var uncertain =
            kind == TemplateKind.AssembledSet
                ? Assembled("old", "old", "old", 1)
                : Source("old", "old", 1);
        uncertain = missingAge ? uncertain with { CreatedAt = null } : uncertain with { Id = "" };
        var newest = Assembled("new", "new", "new", 2);
        TemplateImage[] images = [uncertain, newest];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Contain(uncertain);
        plan.Remove.Should().NotContain(uncertain);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedImagesAreNeverEligible(bool full)
    {
        // Arrange
        var newest = Assembled("new", "new", "new", 2);
        var stale = Assembled("old", "old", "old", 1);
        var source = Source("unused", "unused", 1);
        TemplateImage[] unrelated =
        [
            Unrelated("other", "old", "lookalike", 1),
            Unrelated("agentic-box-rwest-team/squad", "local", "local", 1),
        ];
        TemplateImage[] images = [newest, stale, source, .. unrelated];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Contain(unrelated);
        plan.Remove.Should()
            .BeEquivalentTo<TemplateImage>(full ? [newest, stale, source] : [stale, source]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupAgeIncludesAliasesAndKitsWhilePartitionsPreserveOrderAndDuplicates(bool full)
    {
        // Arrange
        var root = Assembled("new-a", "new-a", "root", 1);
        var alias = Assembled("new-a", "new-a-template", "alias", 4);
        var tiedKit = Assembled("new-b", "new-b-kit-12", "kit", 4);
        var source = Source("shared", "root", 1);
        var stale = Assembled("old", "old", "root", 3);
        var unrelated = Unrelated("other", "new", "other", 5);
        TemplateImage[] images = [stale, root, unrelated, alias, stale, tiedKit, source];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Equal(full ? [unrelated] : [root, unrelated, alias, tiedKit, source]);
        plan.Remove.Should()
            .Equal(full ? [stale, root, alias, stale, tiedKit, source] : [stale, stale]);
    }

    [Theory]
    [InlineData(false, " ")]
    [InlineData(true, " ")]
    [InlineData(false, "\t")]
    [InlineData(true, "\t")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public void BlankAssembledIdentityProtectsItsAliasesAndEveryPotentialSource(
        bool full,
        string? id
    )
    {
        // Arrange
        var uncertain = Assembled("old", "old-kit-10", id!, 1);
        var alias = Assembled("old", "old-template", "alias", 1);
        var source = Source("potential", "source", 1);
        var newest = Assembled("new", "new", "new", 2);
        TemplateImage[] images = [uncertain, alias, source, newest];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Equal(full ? [uncertain, alias, source] : images);
        plan.Remove.Should().Equal(full ? [newest] : []);
    }

    private static TemplateImage Assembled(string set, string tag, string id, int day) =>
        Image($"assembled:{tag}", TemplateKind.AssembledSet, set, id, day);

    private static TemplateImage Source(string tag, string id, int day) =>
        Image($"source:{tag}", TemplateKind.KitSource, string.Empty, id, day);

    private static TemplateImage Unrelated(string repository, string tag, string id, int day) =>
        Image($"{repository}:{tag}", TemplateKind.Unrelated, string.Empty, id, day);

    private static TemplateImage Image(
        string reference,
        TemplateKind kind,
        string set,
        string id,
        int day
    ) => new(reference, kind, set, id, new DateTimeOffset(2026, 11, day, 0, 0, 0, TimeSpan.Zero));
}
