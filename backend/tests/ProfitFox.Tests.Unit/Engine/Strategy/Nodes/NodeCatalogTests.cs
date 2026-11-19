using ProfitFox.Engine.Strategy.Nodes;

namespace ProfitFox.Tests.Unit.Engine.Strategy.Nodes;

/// <summary>
///     The list of kinds the constructor builds its palette from (task 2.2).
/// </summary>
public sealed class NodeCatalogTests
{
    [Test]
    public void All_holds_the_ten_kinds_of_H0001_and_each_is_found_by_name()
    {
        Assert.That(
            NodeCatalog.All.Select(kind => kind.Name),
            Is.EqualTo(new[] { "parameters", "candle", "funding-rate", "compare", "previous", "and", "or", "not", "first-time", "outputs" }));
        Assert.That(NodeCatalog.All.Select(kind => NodeCatalog.Find(kind.Name)), Is.EqualTo(NodeCatalog.All));
    }
}
