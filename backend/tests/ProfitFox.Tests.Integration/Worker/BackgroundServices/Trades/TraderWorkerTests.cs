using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Integration.Application.Commands.Trades;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices.Trades;

/// <summary>
///     Change take-idea-trades, tasks 7.1, 7.3 and 7.4 (design.md, decisions 2 and 3): the trading Worker under its lease, the
///     stream of an account as the main path and REST after the subscription, after a break, as the control and while there is
///     no stream — on <see cref="Exchange.FakeBybit" />.
/// </summary>
[NonParallelizable]
public sealed class TraderWorkerTests
{
    private TraderBench _bench = null!;

    [SetUp]
    public async Task Start()
    {
        _bench = await TraderBench.Start();
    }

    [TearDown]
    public async Task Stop()
    {
        await _bench.DisposeAsync();
    }

    // 7.1: without trades there are no connections and no calls of the exchange.
    [Test]
    public async Task Without_trades_the_Worker_opens_nothing_and_calls_nothing()
    {
        await _bench.Worker();
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.That(_bench.Bybit.CallsNow(), Is.Empty);
        Assert.That(_bench.Main.OpenStreams, Is.Zero);
        Assert.That(_bench.Bybit.PriceSymbols(), Is.Empty);
    }

    // 7.1: a Worker that does not trade starts without the master key; one that trades does not.
    [Test]
    public async Task A_Worker_that_does_not_trade_starts_without_the_master_key_and_a_trading_one_does_not()
    {
        await using (WorkerHost quiet = await WorkerHost.Start(_bench.Server, "quiet", runs: false, masterKey: ""))
        {
            Assert.That(quiet.Name, Is.EqualTo("quiet"));
        }

        Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() => WorkerHost.Start(_bench.Server, "trader", runs: false, bybit: _bench.Bybit, masterKey: ""));
    }

    // Spec idea-trades «Лонг по идее» and «Закрытие по потоку»: the order goes from the Worker, the stream tells the fill and
    // the close by the target, and the trade is closed without a poll.
    [Test]
    public async Task A_long_is_sent_by_the_Worker_and_closed_by_the_target_on_the_stream()
    {
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:10:00"));
        Trade trade = await _bench.Opened();

        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.EqualTo(new Exchange.FakeBybit.Position(TradeSide.Long, 0.4m, 237.40m, 231.38m, 245.54m, 3)));
        Assert.That(await _bench.Feed(), Is.EqualTo("stream"));
        int reads = _bench.CallsOf("execution-list");

        _bench.Bybit.Price("SOLUSDT", 245.60m);
        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);

        Assert.That((closed.CloseReason, closed.Numbers.ExitPrice), Is.EqualTo(((TradeCloseReason?)TradeCloseReason.Target, (decimal?)245.54m)));
        Assert.That(closed.Result, Is.EqualTo(decimal.Round(0.4m * (245.54m - 237.40m), 8) - closed.Numbers.Fees));
        Assert.That(_bench.CallsOf("execution-list"), Is.EqualTo(reads), "no read of REST: the stream told it");
    }

    // 7.3: the subscription goes before the read of REST, and an order of the account goes only after both.
    [Test]
    public async Task The_stream_opens_before_the_read_of_REST_and_the_entry_goes_after_them()
    {
        await _bench.Worker();
        Trade trade = await _bench.Opened();

        string[] calls = [.. _bench.Bybit.CallsNow().Where(call => call.ApiKey == TradeBench.MainKey).Select(call => call.Call)];
        int stream = Array.IndexOf(calls, "stream");
        Assert.That(stream, Is.GreaterThanOrEqualTo(0));
        // The entry itself read the positions of the pair before; after the subscription the Worker reads the fills, the
        // positions, and only then sends the order.
        List<string> after = [.. calls[(stream + 1)..]];
        Assert.That(after.IndexOf("execution-list"), Is.GreaterThanOrEqualTo(0));
        Assert.That(after.IndexOf("position-list"), Is.GreaterThan(after.IndexOf("execution-list")));
        Assert.That(after.IndexOf("order-create"), Is.GreaterThan(after.IndexOf("position-list")));
        Assert.That(calls.Count(call => call == "order-create"), Is.EqualTo(1));
        Assert.That((await _bench.Read(trade.Id)).OpenedAt, Is.Not.Null);
    }

    // 7.3, spec «Запуск с открытой сделкой»: a Worker starting with an open trade opens the stream, reads REST from the cursor
    // and leads the trade on; a stop filled while nobody led it is found.
    [Test]
    public async Task A_Worker_started_with_an_open_trade_reads_what_happened_and_leads_it()
    {
        WorkerHost first = await _bench.Worker("trader-1");
        Trade trade = await _bench.Opened();
        await _bench.Stop(first);

        _bench.Bybit.Price("SOLUSDT", 231.00m);
        await _bench.Worker("trader-2");

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Stop));
    }

    // 7.1, spec «Второй Worker подхватил».
    [Test]
    public async Task When_the_trading_Worker_stops_the_second_takes_the_lease_and_leads_the_trades()
    {
        WorkerHost first = await _bench.Worker("trader-1");
        await _bench.Worker("trader-2", wait: false);
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromSeconds(5)));
        await _bench.Until(trade.Id, TradeState.Open);

        await _bench.Stop(first);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed, TimeSpan.FromSeconds(30));
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Time));
        Assert.That((await _bench.Api.Services.GetRequiredService<ITraderLeaseRepository>().StateAsync(CancellationToken.None))!.Worker, Is.EqualTo("trader-2"));
    }

    // 7.1: the loss of the lease closes the streams.
    [Test]
    public async Task A_lost_lease_closes_the_streams()
    {
        WorkerHost worker = await _bench.Worker();
        await _bench.Opened();
        Assert.That(_bench.Main.OpenStreams, Is.EqualTo(1));

        await worker.Services.GetRequiredService<ITraderLeaseRepository>().ReleaseAsync("trader-1", CancellationToken.None);
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await Dapper.SqlMapper.ExecuteAsync(connection, "UPDATE trader_lease SET worker = 'another', until = now() + interval '1 minute'");
        }

        await Poll.Until("the streams are closed", () => Task.FromResult(_bench.Main.OpenStreams == 0 && _bench.Bybit.PriceSymbols().Count == 0));
    }

    // 7.3: the stream closes StreamIdle after the last trade of the account.
    [Test]
    public async Task The_stream_closes_after_the_last_trade_of_the_account()
    {
        await _bench.Worker();
        Trade trade = await _bench.Opened();

        _bench.Bybit.Price("SOLUSDT", 231.00m);
        await _bench.Until(trade.Id, TradeState.Closed);

        await Poll.Until("the stream is closed", () => Task.FromResult(_bench.Main.OpenStreams == 0));
        Assert.That(await _bench.Feed(), Is.Null);
    }

    // 7.4, spec «Поток оборвался» and «Поток вернулся».
    [Test]
    public async Task A_lost_stream_is_polled_at_once_and_a_restored_one_is_read_once_and_led_by_the_stream_again()
    {
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:10:00"));
        Trade trade = await _bench.Opened();

        _bench.Main.Drop();
        await Poll.Until("the account is polled", async () => await _bench.Feed() == "poll");
        _bench.Bybit.Price("SOLUSDT", 231.00m);
        Trade closed = await _bench.Until(trade.Id, TradeState.Closed, TimeSpan.FromSeconds(5));
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Stop));

        _bench.Bybit.Price("SOLUSDT", 237.40m);
        Trade next = await _bench.Enter(await _bench.Idea());
        await _bench.Until(next.Id, TradeState.Open);
        int reads = _bench.CallsOf("execution-list");
        _bench.Main.Restore();
        await Poll.Until("the account is on the stream", async () => await _bench.Feed() == "stream");
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.That(_bench.CallsOf("execution-list") - reads, Is.EqualTo(1), "one read after the return, then the stream");
    }

    // 7.4: a stream that does not open within StreamOpenWait leaves the account polled, and the entry goes.
    [Test]
    public async Task A_stream_that_does_not_open_leaves_the_account_polled_and_the_entry_goes()
    {
        _bench.Main.StreamRefusal = "the socket is closed";
        await _bench.Worker();

        Trade trade = await _bench.Opened();

        Assert.That(await _bench.Feed(), Is.EqualTo("poll"));
        _bench.Bybit.Price("SOLUSDT", 231.00m);
        Assert.That((await _bench.Until(trade.Id, TradeState.Closed, TimeSpan.FromSeconds(5))).CloseReason, Is.EqualTo(TradeCloseReason.Stop));
    }

    // 7.4: the package gave up — the stream opens anew with a growing pause, and the account is polled meanwhile.
    [Test]
    public async Task A_stream_the_package_gave_up_on_opens_anew_with_a_growing_pause()
    {
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:02"));
        await _bench.Opened();
        _bench.Main.StreamRefusal = "the socket is closed";

        _bench.Main.Close();
        await Poll.Until("the account is polled", async () => await _bench.Feed() == "poll");
        await Poll.Until("three tries to open the stream", () => Task.FromResult(_bench.CallsOf("stream") >= 4), TimeSpan.FromSeconds(15));
        _bench.Main.StreamRefusal = null;

        await Poll.Until("the stream is open again", async () => await _bench.Feed() == "stream", TimeSpan.FromSeconds(15));
        Assert.That(_bench.Main.OpenStreams, Is.EqualTo(1));
    }

    // 7.4: an event the live stream kept silent about is found by the control of REST.
    [Test]
    public async Task An_event_the_live_stream_kept_silent_about_is_found_by_the_control()
    {
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:00:01"));
        Trade trade = await _bench.Opened();

        _bench.Main.Silent = 10;
        _bench.Bybit.Price("SOLUSDT", 231.00m);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed, TimeSpan.FromSeconds(5));
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Stop));
    }

    // 7.4, spec «Сверка после простоя»: a stop filled while no Worker worked closes the trade at the start with the fill of
    // that time.
    [Test]
    public async Task A_stop_filled_while_the_bench_was_down_closes_the_trade_by_the_stop_after_the_start()
    {
        WorkerHost worker = await _bench.Worker();
        Trade trade = await _bench.Opened();
        await _bench.Stop(worker);
        _bench.Bybit.Price("SOLUSDT", 230.00m);
        ExchangeFill stop = _bench.Bybit.FillsOf(TradeBench.MainKey)[^1];

        await _bench.Worker("trader-2");

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That((closed.CloseReason, closed.ClosedAt, closed.Numbers.ExitPrice), Is.EqualTo(((TradeCloseReason?)TradeCloseReason.Stop, (DateTimeOffset?)stop.Time, (decimal?)231.38m)));
    }
}
