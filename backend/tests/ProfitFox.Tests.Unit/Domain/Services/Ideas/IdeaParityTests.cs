using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.Services.Positions;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Positions;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;
using ProfitFox.Tests.Unit.Domain.Services.Trades;

namespace ProfitFox.Tests.Unit.Domain.Services.Ideas;

/// <summary>
///     Change add-ideas-and-market, task 7.3: spec strategy-ideas «Итог идеи» («Как в прогоне») — on the reference minutes of
///     the 1200 trades of <see cref="OutcomeFixture" /> an idea fed minute by minute gives the trade a backtest counts on the
///     same path (<see cref="CycleRun.Simulate" />, which <see cref="Cycle.Open" /> of a backtest calls): its entry, the time
///     and the reason of its exit and its outcome.
/// </summary>
public sealed class IdeaParityTests
{
    [Test]
    public void An_idea_on_the_reference_minutes_is_the_trade_of_the_backtest()
    {
        int compared = 0;
        foreach ((MinutePath Path, DateTimeOffset Entry, TradePlan Plan, IReadOnlyList<(DateTimeOffset Open, double O, double H, double L, double C, bool Covered)> Minutes,
                     IReadOnlyList<(DateTimeOffset Time, double Rate)> Payments) row in OutcomeFixture.TradesWithRows())
        {
            (MinutePath path, DateTimeOffset entry, TradePlan plan) = (row.Path, row.Entry, row.Plan);
            CycleOutcome trade = CycleRun.Simulate(path, entry, plan, TradeCosts.Default, 100);
            if (trade.Status != TradeStatus.Ok)
            {
                continue;
            }

            Idea idea = Fed(entry, plan, row.Minutes, row.Payments);

            string what = $"the trade at {entry:O} of {plan}";
            Assert.That((idea.EntryPrice, idea.EntryTime), Is.EqualTo(((double?)trade.Fills[0].Price, (DateTimeOffset?)trade.Fills[0].Moment)), what);
            Assert.That((idea.ExitTime, Reason(idea.ExitReason), idea.Ambiguous), Is.EqualTo(((DateTimeOffset?)trade.Fills[^1].Moment, trade.ExitReason, trade.Ambiguous)), what);
            Assert.That(idea.Result, Is.EqualTo(trade.Net).Within(1e-15), what);
            Assert.That(idea.ResultUsdt, Is.EqualTo(trade.NetAmount).Within(1e-13), what);
            compared++;
        }

        Assert.That(compared, Is.GreaterThan(1000));
        TestContext.Out.WriteLine($"compared {compared} trades");
    }

    // The idea of the plan fed the minutes with trades of its path in time order, the end of its time and the payments,
    // each at the close the path prices it at: the last close before the minute of the payment.
    private static Idea Fed(
        DateTimeOffset entry,
        TradePlan plan,
        IReadOnlyList<(DateTimeOffset Open, double O, double H, double L, double C, bool Covered)> minutes,
        IReadOnlyList<(DateTimeOffset Time, double Rate)> payments)
    {
        Idea idea = IdeaGenerator.Generate(
            side: plan.Side == TradeSide.Long ? EntrySide.Long : EntrySide.Short,
            moment: entry,
            after: plan.Horizon,
            stop: plan.Stop,
            target: plan.Target);
        foreach ((DateTimeOffset open, double o, double h, double l, double c, bool covered) in minutes)
        {
            if (covered && !double.IsNaN(o) && IdeaCycle.Minute(idea, new ClosedCandle(open, open.AddMinutes(1), Text(o), Text(h), Text(l), Text(c), 1), TradeCosts.Default))
            {
                break;
            }
        }

        IdeaCycle.TimeUp(idea);
        foreach ((DateTimeOffset time, double rate) in payments)
        {
            (DateTimeOffset Open, double O, double H, double L, double C, bool Covered)[] before = [.. minutes.Where(minute => minute.Open <= time.AddMinutes(-1) && !double.IsNaN(minute.C))];
            IdeaCycle.Payment(idea, time, rate, before.Length > 0 ? before[^1].C : double.NaN);
        }

        IdeaCycle.Close(idea, TradeCosts.Default);
        return idea;
    }

    // The decimal the text of the price gives, as a price of the service is read.
    private static decimal Text(double price)
    {
        return decimal.Parse(price.ToString("R", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static CycleExit Reason(ExitReason reason)
    {
        return reason switch
        {
            ExitReason.Stop => CycleExit.Stop,
            ExitReason.Target => CycleExit.Target,
            ExitReason.Time => CycleExit.Time,
            _ => CycleExit.None
        };
    }
}
