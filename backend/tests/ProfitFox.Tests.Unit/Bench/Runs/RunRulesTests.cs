using ProfitFox.Domain.Hypotheses;
using ProfitFox.Domain.Runs;
using ProfitFox.Domain.Trades;

using static ProfitFox.Tests.Unit.Bench.Runs.Designs;

namespace ProfitFox.Tests.Unit.Bench.Runs;

/// <summary>
///     Spec hypothesis-bench: periods, blocks, random entries, the events that count and block means (task 7.2).
/// </summary>
public sealed class RunRulesTests
{
    [Test]
    public void An_event_whose_trade_crosses_the_end_of_the_period_is_left_out()
    {
        RunDesign design = Exploration();

        Assert.That(design.InPeriod(Utc(2026, 5, 15, 20)), Is.True, "ends exactly at the end of exploration");
        Assert.That(design.InPeriod(Utc(2026, 5, 15, 22)), Is.False);
        Assert.That(design.InPeriod(Utc(2026, 5, 16)), Is.False);
    }

    [Test]
    public void The_periods_are_fixed_dates()
    {
        Assert.That(Periods.Span(Period.Holdout, Utc(2026, 11, 18)), Is.EqualTo((Utc(2026, 5, 16), Utc(2026, 11, 16))));
        Assert.That(Periods.Span(Period.NewData, Utc(2026, 11, 18)), Is.EqualTo((Utc(2026, 11, 16), Utc(2026, 11, 18))));
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
    public void Random_entries_stay_in_the_block_off_the_events_on_candle_closes()
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
    public void Random_entries_of_the_last_week_end_inside_the_period()
    {
        DateTimeOffset[] events = [Utc(2026, 5, 13, 8)];

        DateTimeOffset[] entries = Draw("BTCUSDT", events)[0];

        Assert.That(entries, Has.All.LessThanOrEqualTo(Utc(2026, 5, 15, 20)));
    }

    [Test]
    public void An_event_with_too_few_valid_random_entries_does_not_count()
    {
        EventRow row = Row(block: 1, eventY: 0.01, randomYs: [0.0], randomValid: 1);

        Assert.That(row.Usable, Is.False);
    }

    [Test]
    public void A_block_mean_is_the_mean_of_its_events_of_every_pair()
    {
        EventRow[] rows = [Row(block: 7, eventY: 0.005, randomYs: [0.001, 0.001]), Row(block: 7, eventY: 0.0, randomYs: [0.002, 0.002], instrument: "ETHUSDT"), Row(block: 8, eventY: 0.003, randomYs: [0.0, 0.0])];

        IReadOnlyList<(long Block, double Mean, int Events)> means = Evidence.BlockMeans(rows);

        Assert.That(means.Select(mean => (mean.Block, Math.Round(mean.Mean, 12), mean.Events)), Is.EqualTo(new[] { (7L, 0.001, 2), (8L, 0.003, 1) }));
    }

    // The random entries of every event of a pair on exploration: block by block, event by event, from one generator.
    private static IReadOnlyList<DateTimeOffset[]> Draw(string symbol, DateTimeOffset[] events)
    {
        var randoms = new RandomEntries(symbol, events, Exploration());
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

    private static EventRow Row(long block, double eventY, double[] randomYs, int? randomValid = null, string instrument = "BTCUSDT")
    {
        TradeFields Fields(double y) => new TradeFields(y, y, 0, 0, 0, y);
        TradeFields[] randoms = [.. randomYs.Select(Fields)];
        return new EventRow(
            instrument,
            Utc(2026, 1, 5),
            block,
            TradeStatus.Ok,
            Outcome: null,
            Fields(eventY),
            [],
            randomValid ?? randoms.Length,
            TradeFields.Mean(randoms),
            randoms.Length >= 2 ? randoms[0].Y - randoms.Skip(1).Average(trade => trade.Y) : null);
    }
}
