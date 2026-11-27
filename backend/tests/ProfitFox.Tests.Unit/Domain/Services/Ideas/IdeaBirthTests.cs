using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Services.Execution;
using ProfitFox.Domain.Services.Graph;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Strategies.Enums;
using ProfitFox.Tests.Unit.Domain.Services.Graph;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 7.1: spec strategy-ideas «Идея» — «Идея funding-short» and «Стоп ещё не посчитан».
/// </summary>
public sealed class IdeaBirthTests
{
    private static readonly DateTimeOffset Noon = new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void An_order_of_funding_short_on_a_candle_of_BTCUSDT_closed_at_noon_is_an_idea()
    {
        TradingStrategy strategy = StrategyGraph.ReadStrategy(Graphs.H0001Strategy, StrategyKind.Idea).Value!;
        var executor = new StrategyExecutor(strategy);
        var before = new ClosedCandle(Noon.AddHours(-8), Noon.AddHours(-4), 96000, 96300, 95900, 96100, 10);
        var candle = new ClosedCandle(Noon.AddHours(-4), Noon, 96100, 96400, 96000, 96180, 12);
        var id = Guid.NewGuid();

        executor.Step("BTCUSDT", before, new FundingPayment(Noon.AddHours(-8), 0.0001m));
        StrategyStep step = executor.Step("BTCUSDT", candle, new FundingPayment(Noon, 0.0006m));
        Idea idea = IdeaBirth.Of(step, candle, id, "funding-short", 4, FourHours, "BTCUSDT", ["↑ funding"])!;

        Assert.That(
            (idea.Symbol, idea.StrategyId, idea.StrategyName, idea.StrategyVersion, idea.Timeframe, idea.Side, idea.Moment, idea.Until),
            Is.EqualTo(("BTCUSDT", id, "funding-short", 4, FourHours, EntrySide.Short, Noon, Noon.AddHours(4))));
        Assert.That((idea.Stop, idea.Target, idea.Size, idea.CandleClose, idea.ShownEntry), Is.EqualTo(((double?)null, (double?)null, 100m, 96180m, 96180.0)));
        Assert.That(idea.Labels, Is.EqualTo(new[] { "↑ funding" }));
    }

    [Test]
    public void An_order_whose_stop_is_missing_is_no_idea()
    {
        var candle = new ClosedCandle(Noon.AddHours(-4), Noon, 1, 1, 1, 1, 1);
        var step = new StrategyStep(Noon, EntrySide.Long, FourHours, ExitDistance.Of(null), ExitDistance.None);

        Assert.That(IdeaBirth.Of(step, candle, Guid.NewGuid(), "breakout-long", 7, FourHours, "SOLUSDT", []), Is.Null);
        Assert.That(IdeaBirth.Of(step with { Entry = null, Stop = ExitDistance.Of(0.02m) }, candle, Guid.NewGuid(), "breakout-long", 7, FourHours, "SOLUSDT", []), Is.Null, "no order");
        Assert.That(IdeaBirth.Of(step with { Stop = ExitDistance.Of(0.02m) }, candle, Guid.NewGuid(), "breakout-long", 7, FourHours, "SOLUSDT", [])!.Stop, Is.EqualTo(0.02));
    }
}
