using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Market;
using ProfitFox.Application.TradeFlow;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Market;
using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

using Shouldly;

namespace ProfitFox.Tests.Unit.Application.TradeFlow;

/// <summary>The collector while it holds the trade flow: the pairs, the stream, the writes of the minutes and the breaks (decisions 3, 6 and 9).</summary>
public class TradeFlowSessionTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-09T12:04:10Z", CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Minute = DateTimeOffset.Parse("2026-11-09T12:04:00Z", CultureInfo.InvariantCulture);
    private static readonly Instrument Sol = new Instrument(MarketCategory.Linear, "SOLUSDT");
    private static readonly Instrument Btc = new Instrument(MarketCategory.Spot, "BTCUSDT");
    private static readonly Guid Owner = Guid.NewGuid();

    private FakeTimeProvider _time = null!;
    private IScannerPairRepository _pairs = null!;
    private ITradeFlowMinuteRepository _minutes = null!;
    private IStreamBreakRepository _breaks = null!;
    private IMarketStreams _streams = null!;
    private ITradeFlowJournal _journal = null!;
    private List<(IReadOnlyList<Instrument> Instruments, Action<Instrument, TradeEvent> Traded, Action<DateTimeOffset> Lost, Action<DateTimeOffset> Restored)> _subscriptions = null!;
    private List<IReadOnlyCollection<(Instrument Instrument, TradeFlowMinute Minute)>> _saved = null!;
    private IReadOnlyList<ScannerPair> _listed = [];
    private ServiceProvider _meters = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(Start);
        _listed = [new ScannerPair(Sol, true), new ScannerPair(Btc, true)];
        _pairs = Substitute.For<IScannerPairRepository>();
        _pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _listed);
        _minutes = Substitute.For<ITradeFlowMinuteRepository>();
        _saved = [];
        _minutes.SaveAsync(Owner, Arg.Any<IReadOnlyCollection<(Instrument, TradeFlowMinute)>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _saved.Add([.. call.ArgAt<IReadOnlyCollection<(Instrument, TradeFlowMinute)>>(1)]);

                return true;
            });
        _breaks = Substitute.For<IStreamBreakRepository>();
        _streams = Substitute.For<IMarketStreams>();
        _subscriptions = [];
        _streams.SubscribeTradesAsync(
                Arg.Any<IReadOnlyList<Instrument>>(),
                Arg.Any<Action<Instrument, TradeEvent>>(),
                Arg.Any<Action<DateTimeOffset>>(),
                Arg.Any<Action<DateTimeOffset>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (_subscriptions)
                {
                    _subscriptions.Add(
                        (call.ArgAt<IReadOnlyList<Instrument>>(0), call.ArgAt<Action<Instrument, TradeEvent>>(1), call.ArgAt<Action<DateTimeOffset>>(2),
                            call.ArgAt<Action<DateTimeOffset>>(3)));
                }

                return Substitute.For<IAsyncDisposable>();
            });
        _journal = Substitute.For<ITradeFlowJournal>();
        _meters = new ServiceCollection().AddMetrics().BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        _meters.Dispose();
    }

    [Test]
    public async Task Listed_pairs_of_linear_and_spot_are_subscribed_and_moex_and_unlisted_are_not()
    {
        _listed = [new ScannerPair(Sol, true), new ScannerPair(Btc, true), new ScannerPair(new Instrument(MarketCategory.Moex, "SBER"), true), new ScannerPair(new Instrument(MarketCategory.Linear, "OLDUSDT"), false)];
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await WaitAsync(() => Subscriptions().Count == 1);

        Subscriptions()[0].Instruments.ShouldBe([Sol, Btc], ignoreOrder: true);
        await StopAsync(lease, session);
    }

    // Scenario "Минута закрылась".
    [Test]
    public async Task Closed_minute_of_every_pair_goes_to_storage_in_one_batch_within_15_seconds()
    {
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        Trade(Sol, new TradeEvent(Minute.AddSeconds(20), TradeSide.Buy, 2m, 150m));
        Trade(Btc, new TradeEvent(Minute.AddSeconds(30), TradeSide.Sell, 1m, 70000m));
        await AdvanceToAsync(Minute.AddSeconds(75), () => _saved.Count > 0);

        _saved.Single().ShouldBe([(Sol, new TradeFlowMinute(Minute, 2m, 0m, 300m, 0m, 1, 0)), (Btc, new TradeFlowMinute(Minute, 0m, 1m, 0m, 70000m, 0, 1))], ignoreOrder: true);
        await StopAsync(lease, session);
    }

    // Scenario "База недоступна": the write is tried again until storage answers; nothing is lost and the next minutes go on in order.
    [Test]
    public async Task Failed_write_is_tried_again_with_every_minute_kept()
    {
        var failures = 4;
        _minutes.SaveAsync(Owner, Arg.Any<IReadOnlyCollection<(Instrument, TradeFlowMinute)>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (Interlocked.Decrement(ref failures) >= 0)
                {
                    throw new InvalidOperationException("storage down");
                }

                _saved.Add([.. call.ArgAt<IReadOnlyCollection<(Instrument, TradeFlowMinute)>>(1)]);

                return true;
            });
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        Trade(Sol, new TradeEvent(Minute.AddSeconds(20), TradeSide.Buy, 2m, 150m));
        await AdvanceToAsync(Minute.AddSeconds(85), () => Volatile.Read(ref failures) < 0);
        Trade(Sol, new TradeEvent(Minute.AddSeconds(90), TradeSide.Buy, 1m, 151m));
        await AdvanceToAsync(Minute.AddSeconds(135), () => _saved.Count == 2);

        _saved[0].ShouldBe([(Sol, new TradeFlowMinute(Minute, 2m, 0m, 300m, 0m, 1, 0))]);
        _saved[1].ShouldBe([(Sol, new TradeFlowMinute(Minute.AddMinutes(1), 1m, 0m, 151m, 0m, 1, 0))]);
        _journal.Received(4).Failed("minutes", Arg.Any<InvalidOperationException>());
        await StopAsync(lease, session);
    }

    // Decision 2: storage refuses a batch of a collector that lost the lease, and the session ends.
    [Test]
    public async Task Batch_refused_for_the_lease_ends_the_session()
    {
        _minutes.SaveAsync(Owner, Arg.Any<IReadOnlyCollection<(Instrument, TradeFlowMinute)>>(), Arg.Any<CancellationToken>()).Returns(false);
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        Trade(Sol, new TradeEvent(Minute.AddSeconds(20), TradeSide.Buy, 2m, 150m));
        await AdvanceToAsync(Minute.AddSeconds(75), () => session.IsCompleted);

        await Should.ThrowAsync<TradeFlowLeaseLostException>(session);
    }

    // Scenario "Новая пара в списке": the list is read again every 5 minutes and a new pair is subscribed on its own.
    [Test]
    public async Task New_pair_in_the_list_is_subscribed_within_5_minutes_and_its_minutes_are_written()
    {
        var added = new Instrument(MarketCategory.Linear, "NEWUSDT");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        _listed = [.. _listed, new ScannerPair(added, true)];
        await AdvanceToAsync(Start.AddMinutes(5), () => Subscriptions().Count == 2);
        DateTimeOffset minute = Minute.AddMinutes(5);
        Trade(added, new TradeEvent(minute.AddSeconds(30), TradeSide.Buy, 1m, 2m), subscription: 1);
        await AdvanceToAsync(minute.AddSeconds(75), () => _saved.Count > 0);

        Subscriptions()[1].Instruments.ShouldBe([added]);
        _saved.Single().ShouldBe([(added, new TradeFlowMinute(minute, 1m, 0m, 2m, 0m, 1, 0))]);
        await StopAsync(lease, session);
    }

    [Test]
    public async Task Failed_read_of_the_list_is_logged_and_tried_at_the_next_read()
    {
        var added = new Instrument(MarketCategory.Linear, "NEWUSDT");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        var failure = new InvalidOperationException("storage down");
        _pairs.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<IReadOnlyList<ScannerPair>>(failure), Task.FromResult<IReadOnlyList<ScannerPair>>([.. _listed, new ScannerPair(added, true)]));
        await AdvanceToAsync(Start.AddMinutes(10), () => Subscriptions().Count == 2);

        _journal.Received(1).Failed("pairs", failure);
        Subscriptions()[1].Instruments.ShouldBe([added]);
        await StopAsync(lease, session);
    }

    [Test]
    public async Task Refused_subscription_of_a_new_pair_is_logged_and_made_at_the_next_read()
    {
        var added = new Instrument(MarketCategory.Linear, "NEWUSDT");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        var refusal = new ExchangeUnavailableException("refused");
        var refused = false;
        _streams.SubscribeTradesAsync(Arg.Is<IReadOnlyList<Instrument>>(list => list.Contains(added)), Arg.Any<Action<Instrument, TradeEvent>>(), Arg.Any<Action<DateTimeOffset>>(), Arg.Any<Action<DateTimeOffset>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (!refused)
                {
                    refused = true;

                    throw refusal;
                }

                lock (_subscriptions)
                {
                    _subscriptions.Add(([added], (_, _) => { }, _ => { }, _ => { }));
                }

                return Substitute.For<IAsyncDisposable>();
            });
        _listed = [.. _listed, new ScannerPair(added, true)];
        await AdvanceToAsync(Start.AddMinutes(10), () => Subscriptions().Count == 2);

        _journal.Received(1).Failed("pairs", refusal);
        await StopAsync(lease, session);
    }

    // Scenario "Пара снята с торгов": no new minute of the pair is written, and nothing stored is deleted.
    [Test]
    public async Task Pair_out_of_trading_is_no_longer_written()
    {
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        _listed = [new ScannerPair(Sol, true), new ScannerPair(Btc, false)];
        await AdvanceToAsync(Start.AddMinutes(5), () => _pairs.ReceivedCalls().Count() == 2);
        DateTimeOffset minute = Minute.AddMinutes(5);
        Trade(Btc, new TradeEvent(minute.AddSeconds(30), TradeSide.Buy, 1m, 70000m));
        Trade(Sol, new TradeEvent(minute.AddSeconds(30), TradeSide.Buy, 1m, 150m));
        await AdvanceToAsync(minute.AddSeconds(75), () => _saved.Count > 0);

        _saved.Single().ShouldBe([(Sol, new TradeFlowMinute(minute, 1m, 0m, 150m, 0m, 1, 0))]);
        await _minutes.DidNotReceiveWithAnyArgs().ReplaceDayAsync(default, null!, default, null!, default);
        await StopAsync(lease, session);
    }

    // Scenario "Соединение восстановилось".
    [Test]
    public async Task Break_of_the_stream_is_written_when_it_is_back()
    {
        var lost = DateTimeOffset.Parse("2026-11-09T03:10:05Z", CultureInfo.InvariantCulture);
        var restored = DateTimeOffset.Parse("2026-11-09T03:10:41Z", CultureInfo.InvariantCulture);
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        Subscriptions()[0].Lost(lost);
        Subscriptions()[0].Restored(restored);
        await AdvanceToAsync(Start.AddSeconds(10), () => _breaks.ReceivedCalls().Any());

        await _breaks.Received(1).AddAsync(new StreamBreak("trades", lost, restored), Arg.Any<CancellationToken>());
        await StopAsync(lease, session);
    }

    // Scenario "Перезапуск при выкатке": the break runs from the end of the last stored minute to the subscription of the new holder.
    [Test]
    public async Task New_holder_writes_a_break_from_the_end_of_the_last_stored_minute_to_its_subscription()
    {
        var subscribed = DateTimeOffset.Parse("2026-11-09T14:05:10Z", CultureInfo.InvariantCulture);
        _time.SetUtcNow(subscribed);
        _minutes.LastAsync(Arg.Any<CancellationToken>()).Returns(DateTimeOffset.Parse("2026-11-09T14:01:00Z", CultureInfo.InvariantCulture));
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);

        await AdvanceToAsync(subscribed.AddSeconds(10), () => _breaks.ReceivedCalls().Any());

        await _breaks.Received(1).AddAsync(
            new StreamBreak("trades", DateTimeOffset.Parse("2026-11-09T14:02:00Z", CultureInfo.InvariantCulture), subscribed),
            Arg.Any<CancellationToken>());
        await StopAsync(lease, session);
    }

    [Test]
    public async Task First_holder_without_stored_minutes_writes_no_break()
    {
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        await AdvanceToAsync(Start.AddSeconds(30), () => false, mayTimeOut: true);

        _breaks.ReceivedCalls().ShouldBeEmpty();
        await StopAsync(lease, session);
    }

    [Test]
    public async Task Failed_write_of_a_break_is_logged_and_the_next_break_goes_on()
    {
        var failure = new InvalidOperationException("storage down");
        _breaks.AddAsync(Arg.Any<StreamBreak>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(failure), Task.CompletedTask);
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        Subscriptions()[0].Lost(Start.AddMinutes(-3));
        Subscriptions()[0].Restored(Start.AddMinutes(-2));
        await AdvanceToAsync(Start.AddSeconds(10), () => _breaks.ReceivedCalls().Count() == 1);
        Subscriptions()[0].Lost(Start.AddMinutes(-1));
        Subscriptions()[0].Restored(Start);
        await AdvanceToAsync(Start.AddSeconds(20), () => _breaks.ReceivedCalls().Count() == 2);

        _journal.Received(1).Failed("stream-breaks", failure);
        await StopAsync(lease, session);
    }

    // Requirement "Счётчик сделок потока": a trade the collector took counts, a trade of a pair out of trading does not.
    [Test]
    public async Task Taken_trades_are_counted_by_category()
    {
        _listed = [new ScannerPair(Sol, true), new ScannerPair(Btc, true), new ScannerPair(new Instrument(MarketCategory.Spot, "OLDUSDT"), false)];
        using var collector = new MetricCollector<long>(_meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>(), "ProfitFox.TradeFlow", "profitfox.tradeflow.trades");
        using var lease = new CancellationTokenSource();
        Task session = Session().RunAsync(lease.Token);
        await WaitAsync(() => Subscriptions().Count == 1);

        Trade(Sol, new TradeEvent(Minute.AddSeconds(20), TradeSide.Buy, 2m, 150m));
        Trade(Sol, new TradeEvent(Minute.AddSeconds(21), TradeSide.Sell, 2m, 150m));
        Trade(Btc, new TradeEvent(Minute.AddSeconds(22), TradeSide.Sell, 1m, 70000m));
        Trade(new Instrument(MarketCategory.Spot, "OLDUSDT"), new TradeEvent(Minute.AddSeconds(23), TradeSide.Sell, 1m, 1m));

        collector.GetMeasurementSnapshot().GroupBy(measurement => (string)measurement.Tags["category"]!).Select(group => (group.Key, group.Sum(measurement => measurement.Value)))
            .ShouldBe([("linear", 2L), ("spot", 1L)], ignoreOrder: true);
        await StopAsync(lease, session);
    }

    private TradeFlowSession Session()
    {
        return new TradeFlowSession(_pairs, _minutes, _breaks, _streams, new TradeFlowMetrics(_meters.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()), new WorkerIdentity(Owner), _journal, _time);
    }

    private List<(IReadOnlyList<Instrument> Instruments, Action<Instrument, TradeEvent> Traded, Action<DateTimeOffset> Lost, Action<DateTimeOffset> Restored)> Subscriptions()
    {
        lock (_subscriptions)
        {
            return [.. _subscriptions];
        }
    }

    private void Trade(Instrument instrument, TradeEvent trade, int subscription = 0)
    {
        _time.SetUtcNow(trade.Time > _time.GetUtcNow() ? trade.Time : _time.GetUtcNow());
        Subscriptions()[subscription].Traded(instrument, trade);
    }

    private static async Task StopAsync(CancellationTokenSource lease, Task session)
    {
        await lease.CancelAsync();
        (await Should.ThrowAsync<Exception>(session)).ShouldBeAssignableTo<OperationCanceledException>();
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>Moves the clock a second at a time up to <paramref name="until" />, giving the lanes time to run, then waits for the condition.</summary>
    private async Task AdvanceToAsync(DateTimeOffset until, Func<bool> condition, bool mayTimeOut = false)
    {
        while (_time.GetUtcNow() < until)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }

        DateTime wall = DateTime.UtcNow + TimeSpan.FromSeconds(mayTimeOut ? 0.2 : 10);

        while (!condition())
        {
            if (DateTime.UtcNow > wall)
            {
                if (mayTimeOut)
                {
                    return;
                }

                throw new TimeoutException("The condition did not hold in time.");
            }

            await Task.Delay(10);
        }
    }
}