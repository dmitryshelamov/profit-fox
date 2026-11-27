using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using ProfitFox.Application.Commands.Backtests.ExecuteBacktest;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Tests.Unit.Application.Commands.Backtests.ExecuteBacktest;

/// <summary>
///     Task 12.12: a backtest repeats a read the market-data service dropped three times, after growing pauses on the clock of
///     the bench; a refused window and a cancelled read are not repeated.
/// </summary>
public sealed class RepeatingHistorySourceTests
{
    private static readonly DateTimeOffset From = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly HistoryRead<HistoryPayment> Read = new HistoryRead<HistoryPayment>([new TimeRange(From, From.AddDays(1))], [new HistoryPayment(From, "0.0001")]);

    [Test]
    public void The_pauses_grow()
    {
        Assert.That(HistoryRepeats.DefaultPauses, Has.Count.EqualTo(3));
        Assert.That(HistoryRepeats.DefaultPauses, Is.Ordered.Ascending.And.Unique);
        Assert.That(HistoryRepeats.DefaultPauses[0], Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public async Task A_read_dropped_twice_is_read_on_the_second_repeat_after_the_first_two_pauses()
    {
        var source = new ScriptedSource(Dropped(), Dropped());
        var time = new FakeTimeProvider(From);
        IHistorySource repeating = Repeating(source, time);

        Task<HistoryRead<HistoryPayment>> reading = repeating.ReadFunding("btc", From, From.AddDays(1), CancellationToken.None);

        Assert.That(source.Calls, Is.EqualTo(1));
        time.Advance(HistoryRepeats.DefaultPauses[0] - TimeSpan.FromTicks(1));
        await Task.Delay(50);
        Assert.That(source.Calls, Is.EqualTo(1), "no repeat before the first pause is over");
        time.Advance(TimeSpan.FromTicks(1));
        await Until(() => source.Calls == 2);
        time.Advance(HistoryRepeats.DefaultPauses[1]);
        Assert.That(await reading, Is.SameAs(Read), "the read of the repeat, nothing of the dropped ones");
        Assert.That(source.Calls, Is.EqualTo(3));
    }

    [Test]
    public async Task A_read_always_dropped_fails_after_three_repeats()
    {
        var source = new ScriptedSource(Dropped(), Dropped(), Dropped(), Dropped());
        var time = new FakeTimeProvider(From);

        Task<HistoryRead<HistoryPayment>> reading = Repeating(source, time).ReadFunding("btc", From, From.AddDays(1), CancellationToken.None);
        for (int repeat = 0; repeat < HistoryRepeats.DefaultPauses.Count; repeat++)
        {
            await Until(() => source.Calls == repeat + 1);
            time.Advance(HistoryRepeats.DefaultPauses[repeat]);
        }

        HistoryUnavailableException? failure = Assert.ThrowsAsync<HistoryUnavailableException>(async () => await reading);
        Assert.That(failure!.Message, Does.StartWith("dropped").And.EndWith("(repeated 3 times)"));
        Assert.That(source.Calls, Is.EqualTo(4));
    }

    [Test]
    public void A_refused_window_is_not_repeated()
    {
        var refused = new HistoryUnavailableException("the window must end at 2026-01-06T00:00 or before");
        var source = new ScriptedSource(refused);

        HistoryUnavailableException? failure = Assert.ThrowsAsync<HistoryUnavailableException>(
            () => Repeating(source, new FakeTimeProvider(From)).ReadFunding("btc", From, From.AddDays(1), CancellationToken.None));

        Assert.That(failure, Is.SameAs(refused));
        Assert.That(source.Calls, Is.EqualTo(1));
    }

    /// <summary>
    ///     Change add-idea-score, task 5.1: a dropped read of open interest is repeated as one of funding.
    /// </summary>
    [Test]
    public async Task A_dropped_read_of_open_interest_is_repeated()
    {
        var source = new ScriptedSource(new HistoryUnavailableException("dropped", new IOException(), dropped: true));
        var time = new FakeTimeProvider(From);
        Task<HistoryRead<OpenInterestPoint>> reading = Repeating(source, time).ReadOpenInterest("btc", From, From.AddDays(1), CancellationToken.None);

        await Until(() => source.Calls == 1);
        time.Advance(HistoryRepeats.DefaultPauses[0]);

        Assert.That((await reading).Items.Single().Value, Is.EqualTo(52345.123m));
        Assert.That(source.Calls, Is.EqualTo(2));
    }

    [Test]
    public void A_cancelled_read_is_not_repeated()
    {
        var source = new ScriptedSource(new OperationCanceledException());

        Assert.That(
            () => Repeating(source, new FakeTimeProvider(From)).ReadFunding("btc", From, From.AddDays(1), CancellationToken.None),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task A_backtest_cancelled_in_a_pause_stops_without_a_repeat()
    {
        var source = new ScriptedSource(Dropped());
        using var cancellation = new CancellationTokenSource();

        Task<HistoryRead<HistoryPayment>> reading = Repeating(source, new FakeTimeProvider(From)).ReadFunding("btc", From, From.AddDays(1), cancellation.Token);
        await cancellation.CancelAsync();

        Assert.That(async () => await reading, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(source.Calls, Is.EqualTo(1));
    }

    // The continuation of a pause may run on another thread after the clock is moved.
    private static async Task Until(Func<bool> condition)
    {
        for (int wait = 0; !condition(); wait++)
        {
            Assert.That(wait, Is.LessThan(500), "the condition did not come within 5 seconds");
            await Task.Delay(10);
        }
    }

    private static IHistorySource Repeating(IHistorySource source, TimeProvider time)
    {
        return new RepeatingHistorySource(source, new HistoryRepeats(), time, NullLogger.Instance);
    }

    private static HistoryUnavailableException Dropped()
    {
        return new HistoryUnavailableException("dropped", dropped: true);
    }

    // Throws its errors in order, one a read, and then reads.
    private sealed class ScriptedSource(params Exception[] errors) : IHistorySource
    {
        public int Calls { get; private set; }

        public Task<HistoryRead<HistoryCandle>> ReadCandles(string instrumentId, TimeSpan interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
        {
            throw new NotSupportedException();
        }

        public Task<HistoryRead<HistoryPayment>> ReadFunding(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
        {
            int call = Calls++;
            return call < errors.Length ? Task.FromException<HistoryRead<HistoryPayment>>(errors[call]) : Task.FromResult(Read);
        }

        public Task<HistoryRead<OpenInterestPoint>> ReadOpenInterest(string instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellation)
        {
            int call = Calls++;
            return call < errors.Length
                ? Task.FromException<HistoryRead<OpenInterestPoint>>(errors[call])
                : Task.FromResult(new HistoryRead<OpenInterestPoint>([new TimeRange(from, to)], [new OpenInterestPoint(from, 52345.123m)]));
        }
    }
}
