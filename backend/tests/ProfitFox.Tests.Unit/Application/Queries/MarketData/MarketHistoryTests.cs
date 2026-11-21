using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.MarketData;
using ProfitFox.Domain.Services.MarketData;
using ProfitFox.Domain.ValueObjects.Execution;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Queries.MarketData;

/// <summary>
///     Spec market-history: candles of the signal, coverage, the end of the period and the hash.
/// </summary>
public sealed class MarketHistoryTests
{
    private static readonly DateTimeOffset Start = FakeHistorySource.CoveredFrom;
    private static readonly TimeSpan FourHours = TimeSpan.FromHours(4);
    private static readonly DateTimeOffset PeriodEnd = Start.AddDays(30);

    [Test]
    public async Task A_candle_without_trades_does_not_reach_the_executor()
    {
        var source = new FakeHistorySource();
        source.Quiet.Add(Start.AddHours(8));

        (_, IReadOnlyList<ClosedCandle> candles) = await History(source).TradedCandles("btc", FourHours, Start, Start.AddDays(1), CancellationToken.None);

        Assert.That(candles.Select(candle => candle.OpenTime), Is.EqualTo(new[] { 0, 4, 12, 16, 20 }.Select(hours => Start.AddHours(hours))));
        Assert.That(candles[0].CloseTime, Is.EqualTo(Start.AddHours(4)));
    }

    [Test]
    public async Task A_hole_in_the_history_stays_out_of_the_coverage()
    {
        var source = new FakeHistorySource { Hole = new TimeRange(Start.AddHours(10), Start.AddHours(11)) };

        HistoryRead<HistoryCandle> read = await History(source).Candles("btc", TimeSpan.FromMinutes(1), Start.AddHours(9), Start.AddHours(12), CancellationToken.None);

        Assert.That(read.Coverage, Is.EqualTo(new[]
        {
            new TimeRange(Start.AddHours(9), Start.AddHours(10)),
            new TimeRange(Start.AddHours(11), Start.AddHours(12))
        }));
        Assert.That(read.Items.Any(candle => candle.OpenTime >= Start.AddHours(10) && candle.OpenTime < Start.AddHours(11)), Is.False);
    }

    [Test]
    public void A_window_after_the_end_of_the_period_is_refused()
    {
        var source = new FakeHistorySource();

        Assert.That(
            () => History(source).Candles("btc", FourHours, PeriodEnd.AddDays(-1), PeriodEnd.AddHours(4), CancellationToken.None),
            Throws.InstanceOf<PeriodBoundaryException>());
        Assert.That(source.Reads, Is.Empty);
    }

    [Test]
    public async Task Only_the_window_asked_for_is_read()
    {
        var source = new FakeHistorySource();

        await History(source).Candles("btc", TimeSpan.FromMinutes(1), Start.AddDays(2).AddHours(3), Start.AddDays(9).AddHours(4), CancellationToken.None);

        Assert.That(source.Reads, Is.EqualTo(new[] { ("candles", Start.AddDays(2).AddHours(3), Start.AddDays(9).AddHours(4)) }));
    }

    [Test]
    public async Task Reading_the_same_windows_again_gives_the_same_hash()
    {
        var source = new FakeHistorySource();

        string first = await Hash(source);
        string second = await Hash(source);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Has.Length.EqualTo(64));
    }

    [Test]
    public async Task Different_history_gives_a_different_hash()
    {
        string plain = await Hash(new FakeHistorySource());
        var quiet = new FakeHistorySource();
        quiet.Quiet.Add(Start.AddHours(4));

        Assert.That(await Hash(quiet), Is.Not.EqualTo(plain));
    }

    private static async Task<string> Hash(FakeHistorySource source)
    {
        using var hash = new HistoryHash();
        var history = new MarketHistory(source, PeriodEnd, hash);
        await history.Candles("btc", FourHours, Start.AddHours(4), Start.AddDays(2), CancellationToken.None);
        await history.Candles("btc", TimeSpan.FromMinutes(1), Start.AddHours(5), Start.AddHours(7), CancellationToken.None);
        await history.Funding("btc", Start, Start.AddDays(2), CancellationToken.None);
        return hash.Result();
    }

    private static MarketHistory History(FakeHistorySource source)
    {
        return new MarketHistory(source, PeriodEnd, new HistoryHash());
    }
}
