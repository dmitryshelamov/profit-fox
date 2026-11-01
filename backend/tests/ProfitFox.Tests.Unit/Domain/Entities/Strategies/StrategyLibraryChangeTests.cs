using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Entities.Strategies;

/// <summary>The change a library hands to the repository: what its operations saved and removed, with the new versions.</summary>
public class StrategyLibraryChangeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-01-01T00:00:00Z");
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid NodeId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Test]
    public void Loaded_library_has_no_change()
    {
        StrategyLibraryChange change = new StrategyLibrary(3, [], []).Change;

        change.SavedStrategies.ShouldBeEmpty();
        change.RemovedStrategies.ShouldBeEmpty();
        change.SavedComposites.ShouldBeEmpty();
        change.RemovedComposites.ShouldBeEmpty();
        change.NewVersions.ShouldBeEmpty();
    }

    [Test]
    public void Added_and_changed_strategy_is_saved_once_in_its_last_state_with_its_versions()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddStrategy(Id, "A", Definition(1m), Now);

        Strategy changed = library.UpdateStrategy(Id, "B", Definition(2m), Now.AddHours(1))!;

        library.Change.SavedStrategies.ShouldBe([changed]);
        library.Change.NewVersions.Select(version => version.Number).ShouldBe([1, 2]);
    }

    [Test]
    public void Removed_strategy_is_removed_and_no_longer_saved()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddStrategy(Id, "A", Definition(1m), Now);

        library.RemoveStrategy(Id);

        library.Change.SavedStrategies.ShouldBeEmpty();
        library.Change.RemovedStrategies.ShouldBe([Id]);
    }

    [Test]
    public void Added_changed_and_cloned_nodes_are_saved()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(NodeId, "A", Node(), Now);
        CompositeNode changed = library.UpdateComposite(NodeId, "B", Node(), Now)!;

        CompositeNode clone = library.CloneComposite(new CustomNodeId(NodeId), Id, "C", Now)!;

        library.Change.SavedComposites.ShouldBe([changed, clone]);
    }

    [Test]
    public void Removed_node_is_removed_and_no_longer_saved()
    {
        var library = new StrategyLibrary(0, [], []);
        library.AddComposite(NodeId, "A", Node(), Now);

        library.RemoveComposite(NodeId);

        library.Change.SavedComposites.ShouldBeEmpty();
        library.Change.RemovedComposites.ShouldBe([NodeId]);
    }

    private static StrategyDefinition Definition(decimal value)
    {
        return StrategyDefinitions.Long(new GraphBuilder().Node("not", SimpleNodeKind.Not, ("value", Values.Boolean(value > 1m))).Build());
    }

    private static CompositeDefinition Node()
    {
        return new CompositeDefinition([], [], new GraphBuilder().Node("candle", SimpleNodeKind.Candle).Build());
    }
}