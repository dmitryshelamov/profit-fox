using ProfitFox.Domain.Services.Runs;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Runs;
using ProfitFox.Domain.ValueObjects.Runs.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

using static ProfitFox.Tests.Unit.Domain.Services.Runs.Designs;

namespace ProfitFox.Tests.Unit.Domain.Services.Runs;

/// <summary>
///     Spec strategy-backtest «Случайные входы» and «Разница со случайным входом»: weeks, the history of an order, random
///     entries and the orders that count.
/// </summary>
public sealed class RunRulesTests
{
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);

    [Test]
    public void An_order_whose_trade_crosses_the_end_of_history_is_left_out()
    {
        BacktestDesign design = Exploration();

        Assert.That(design.Fits(Utc(2026, 5, 15, 20), FourHours), Is.True, "ends exactly at the end of history");
        Assert.That(design.Fits(Utc(2026, 5, 15, 22), FourHours), Is.False);
        Assert.That(design.Fits(Utc(2026, 5, 16), FourHours), Is.False);
    }

    [Test]
    public void A_week_starts_on_Monday()
    {
        long week = Blocks.Of(Utc(2026, 1, 7, 13), TimeBlock.Week);

        Assert.That(Blocks.Start(week, TimeBlock.Week), Is.EqualTo(Utc(2026, 1, 5)));
        Assert.That(Utc(2026, 1, 5).DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(Blocks.Of(Utc(2026, 1, 4, 23), TimeBlock.Week), Is.EqualTo(week - 1));
        Assert.That(Blocks.Of(Utc(2026, 1, 11, 23), TimeBlock.Week), Is.EqualTo(week));
    }

    [Test]
    public void The_draw_of_random_entries_repeats()
    {
        DateTimeOffset[] events = [Utc(2026, 1, 5, 8), Utc(2026, 1, 5, 16), Utc(2026, 1, 14, 4)];

        IReadOnlyList<DateTimeOffset[]> first = Draw("BTCUSDT", events);
        IReadOnlyList<DateTimeOffset[]> second = Draw("BTCUSDT", events);
        IReadOnlyList<DateTimeOffset[]> other = Draw("ETHUSDT", events);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(other, Is.Not.EqualTo(first));
    }

    [Test]
    public void Random_entries_stay_in_the_week_off_the_orders_on_candle_closes()
    {
        DateTimeOffset[] events = [Utc(2026, 1, 5, 8), Utc(2026, 1, 5, 16)];

        IReadOnlyList<DateTimeOffset[]> entries = Draw("BTCUSDT", events);

        DateTimeOffset[] all = [.. entries.SelectMany(entry => entry)];
        Assert.That(entries.Select(entry => entry.Length), Is.EqualTo(new[] { 20, 20 }));
        Assert.That(all, Has.None.EqualTo(events[0]).And.None.EqualTo(events[1]));
        Assert.That(all, Has.All.Matches<DateTimeOffset>(entry => entry >= Utc(2026, 1, 5) && entry < Utc(2026, 1, 12)));
        Assert.That(all, Has.All.Matches<DateTimeOffset>(entry => entry.Hour % 4 == 0 && entry.Minute == 0));
    }

    [Test]
    public void Random_entries_of_the_last_week_end_inside_history()
    {
        DateTimeOffset[] events = [Utc(2026, 5, 13, 8)];

        DateTimeOffset[] entries = Draw("BTCUSDT", events)[0];

        Assert.That(entries, Has.All.LessThanOrEqualTo(Utc(2026, 5, 15, 20)));
    }

    [Test]
    public void An_order_with_too_few_valid_random_entries_does_not_count()
    {
        EventRow row = Row(randomValid: 1);

        Assert.That(row.Usable, Is.False);
        Assert.That((row with { RandomValid = 2 }).Usable, Is.True);
    }

    // The random entries of every order of a pair on exploration: week by week, order by order, from one generator.
    private static IReadOnlyList<DateTimeOffset[]> Draw(string symbol, DateTimeOffset[] events)
    {
        var randoms = new RandomEntries(symbol, events, Exploration(), _ => FourHours);
        var entries = new DateTimeOffset[events.Length][];
        foreach (IGrouping<long, int> block in Enumerable.Range(0, events.Length).GroupBy(index => Blocks.Of(events[index], TimeBlock.Week)).OrderBy(group => group.Key))
        {
            IReadOnlyList<DateTimeOffset> candidates = randoms.Candidates(block.Key);
            foreach (int index in block.OrderBy(index => events[index]))
            {
                entries[index] = randoms.Draw(candidates);
            }
        }

        return entries;
    }

    private static EventRow Row(int randomValid)
    {
        var fields = new TradeFields(0.01, 0.01, 0, 0, 0, 0.01);
        return new EventRow("BTCUSDT", Utc(2026, 1, 5), 1, TradeStatus.Ok, Outcome: null, fields, [], randomValid, fields);
    }
}
