using ProfitFox.Application.Market;
using ProfitFox.Application.Paging;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Tests.Shared.Generators.Domain.ValueObject.Market;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.Market;

/// <summary>Requirement "Фильтр, поиск и сортировка": the order of the rows of «Рынок».</summary>
public class MarketOrderTests
{
    private static readonly PairMetrics None = MarketSnapshotRowGenerator.NoMetrics;

    private static MarketSnapshotRow[] Growth()
    {
        return
        [
            Row("BBB", None with { Change1h = -1.5m }),
            Row("CCC", None),
            Row("AAA", None with { Change1h = 3m }),
            Row("DDD", None with { Change1h = 10.25m })
        ];
    }

    // Scenario "Сортировка по изменению за 1 час".
    [Test]
    public void The_largest_growth_comes_first_when_descending_and_pairs_without_the_metric_last()
    {
        Symbols(MarketOrder.Sort(Growth(), MarketSortColumn.Change1h, OrderDirection.Descending)).ShouldBe(["DDD", "AAA", "BBB", "CCC"]);
    }

    [Test]
    public void The_largest_fall_comes_first_when_ascending_and_pairs_without_the_metric_still_last()
    {
        Symbols(MarketOrder.Sort(Growth(), MarketSortColumn.Change1h, OrderDirection.Ascending)).ShouldBe(["BBB", "AAA", "DDD", "CCC"]);
    }

    [Test]
    public void Numbers_go_by_value_not_by_their_text()
    {
        MarketSnapshotRow[] rows =
        [
            Row("A", None with { Turnover24h = 900m }),
            Row("B", None with { Turnover24h = 1000000m }),
            Row("C", None with { Turnover24h = 25000m })
        ];

        Symbols(MarketOrder.Sort(rows, MarketSortColumn.Turnover24h, OrderDirection.Descending)).ShouldBe(["B", "C", "A"]);
    }

    [Test]
    public void The_symbol_column_goes_by_symbol_in_both_directions()
    {
        Symbols(MarketOrder.Sort(Growth(), MarketSortColumn.Symbol, OrderDirection.Ascending)).ShouldBe(["AAA", "BBB", "CCC", "DDD"]);
        Symbols(MarketOrder.Sort(Growth(), MarketSortColumn.Symbol, OrderDirection.Descending)).ShouldBe(["DDD", "CCC", "BBB", "AAA"]);
    }

    [Test]
    public void Symbols_are_compared_ordinally()
    {
        MarketSnapshotRow[] rows = [Row("B_B"), Row("BAB"), Row("B1B")];

        Symbols(MarketOrder.Sort(rows, MarketSortColumn.Symbol, OrderDirection.Ascending)).ShouldBe(["B1B", "BAB", "B_B"]);
    }

    [TestCase(OrderDirection.Ascending)]
    [TestCase(OrderDirection.Descending)]
    public void Equal_values_go_by_category_then_symbol_in_either_direction(OrderDirection direction)
    {
        MarketSnapshotRow[] rows =
        [
            Row("ZZZ", MarketCategory.Linear, None with { Price = 1m }),
            Row("AAA", MarketCategory.Spot, None with { Price = 1m }),
            Row("CCC", MarketCategory.Linear, None with { Price = 1m }),
            Row("AAA", MarketCategory.Linear, None with { Price = 1m })
        ];

        MarketOrder.Sort(rows, MarketSortColumn.Price, direction).Select(row => $"{row.Instrument.Category}/{row.Instrument.Symbol}")
            .ShouldBe(["Linear/AAA", "Linear/CCC", "Linear/ZZZ", "Spot/AAA"]);
    }

    [Test]
    public void Rows_without_the_value_also_go_by_category_then_symbol()
    {
        Symbols(MarketOrder.Sort([Row("YYY"), Row("XXX")], MarketSortColumn.Spread, OrderDirection.Descending)).ShouldBe(["XXX", "YYY"]);
    }

    [TestCase("symbol", MarketSortColumn.Symbol)]
    [TestCase("price", MarketSortColumn.Price)]
    [TestCase("change5m", MarketSortColumn.Change5m)]
    [TestCase("change15m", MarketSortColumn.Change15m)]
    [TestCase("change1h", MarketSortColumn.Change1h)]
    [TestCase("change4h", MarketSortColumn.Change4h)]
    [TestCase("change24h", MarketSortColumn.Change24h)]
    [TestCase("turnover1h", MarketSortColumn.Turnover1h)]
    [TestCase("turnover24h", MarketSortColumn.Turnover24h)]
    [TestCase("openInterest", MarketSortColumn.OpenInterest)]
    [TestCase("openInterestChange1h", MarketSortColumn.OpenInterestChange1h)]
    [TestCase("openInterestChange24h", MarketSortColumn.OpenInterestChange24h)]
    [TestCase("fundingRate", MarketSortColumn.FundingRate)]
    [TestCase("spread", MarketSortColumn.Spread)]
    public void A_column_is_read_by_its_code(string code, MarketSortColumn expected)
    {
        MarketOrder.TryParseColumn(code, out MarketSortColumn column).ShouldBeTrue();
        column.ShouldBe(expected);
    }

    [Test]
    public void An_unknown_column_is_not_read()
    {
        MarketOrder.TryParseColumn("volume", out _).ShouldBeFalse();
    }

    private static MarketSnapshotRow Row(string symbol, PairMetrics? metrics = null)
    {
        return MarketSnapshotRowGenerator.Generate(symbol, metrics: metrics);
    }

    private static MarketSnapshotRow Row(string symbol, MarketCategory category, PairMetrics metrics)
    {
        return MarketSnapshotRowGenerator.Generate(symbol, category, metrics);
    }

    private static string[] Symbols(IEnumerable<MarketSnapshotRow> rows)
    {
        return [.. rows.Select(row => row.Instrument.Symbol)];
    }
}