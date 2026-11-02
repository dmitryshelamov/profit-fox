using System.Text.Json;

using ProfitFox.Application.Strategies;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Backtests;
using ProfitFox.Domain.Services.Strategies.BuiltIns;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;
using ProfitFox.Tests.Shared.Builders.Backtests;
using ProfitFox.Tests.Shared.Builders.Strategies;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Unit.Domain.Services.Backtests;

/// <summary>Requirements "Детерминизм прогона" and "Без заглядывания вперёд в прогоне".</summary>
public class ReplayDeterminismTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2024-01-01T00:00:00Z");

    // Scenario "Будущие свечи не влияют".
    [Test]
    public void Fills_up_to_a_candle_do_not_depend_on_the_candles_after_it()
    {
        Candle[] common = Wave(12);
        Candle[] up = [.. common, .. Prices(12, 120m, 125m, 130m)];
        Candle[] down = [.. common, .. Prices(12, 80m, 70m, 60m)];

        IReadOnlyList<(BacktestOrder Order, BacktestFill Fill)> first = Details(up).AllFills();
        IReadOnlyList<(BacktestOrder Order, BacktestFill Fill)> second = Details(down).AllFills();

        DateTimeOffset last = common[^1].OpenTime;
        first.Where(entry => entry.Fill.At <= last).Select(entry => (entry.Order.Id, entry.Fill))
            .ShouldBe(second.Where(entry => entry.Fill.At <= last).Select(entry => (entry.Order.Id, entry.Fill)));
        first.Count(entry => entry.Fill.At <= last).ShouldBeGreaterThan(2);
    }

    [Test]
    public void Second_run_of_ladder_gives_the_same_fills_and_metrics()
    {
        Candle[] candles = Wave(48);

        BacktestDetails first = Details(candles);
        BacktestDetails second = Details(candles);

        JsonSerializer.Serialize(second.Positions).ShouldBe(JsonSerializer.Serialize(first.Positions));
        second.Metrics.ShouldBe(first.Metrics);
        first.AllFills().Count.ShouldBeGreaterThan(4);
    }

    private static BacktestDetails Details(Candle[] candles)
    {
        var details = new DetailsObserver(1000m);
        new BacktestReplay(
                StrategySnapshot.Of(StrategyJson.ParseStrategy(StrategyFixtures.Element("ladder")), BuiltInComposites.Catalog),
                new Dictionary<string, NodeValue>(),
                Conditions.Reference,
                0m,
                LimitFillMode.Breakout,
                Interval.OneHour)
            .Run(Start, candles, details);

        return details.Details();
    }

    /// <summary>Prices going down and up by 2 % around 100, so that ladder buys and sells.</summary>
    private static Candle[] Wave(int count)
    {
        return [.. Enumerable.Range(0, count).Select(hour => Candle(hour, 100m + (hour % 4 < 2 ? -2m : 2m) * (hour % 2 + 1)))];
    }

    private static IEnumerable<Candle> Prices(int fromHour, params decimal[] prices)
    {
        return prices.Select((price, index) => Candle(fromHour + index, price));
    }

    private static Candle Candle(int hour, decimal price)
    {
        return new Candle(Start.AddHours(hour), new Price(price), new Price(price + 1m), new Price(price - 1m), new Price(price), new Volume(1m), new Volume(1m));
    }
}