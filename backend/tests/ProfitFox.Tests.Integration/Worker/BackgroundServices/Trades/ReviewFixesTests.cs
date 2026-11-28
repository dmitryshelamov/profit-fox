using Dapper;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Commands.Trades.CloseTrade;
using ProfitFox.Application.Commands.Trades.ResolveTrade;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Integration.Application.Commands.Trades;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices.Trades;

/// <summary>
///     Change take-idea-trades, group 12: the fixes of the review of the application — each finding by a scenario of the
///     trading Worker on <see cref="Exchange.FakeBybit" /> that fails without its fix.
/// </summary>
[NonParallelizable]
public sealed class ReviewFixesTests
{
    private TraderBench _bench = null!;

    [TearDown]
    public async Task Stop()
    {
        await _bench.DisposeAsync();
    }

    // 12.1 (finding 1): the read of REST after the subscription fails — Bybit answers neither reads nor writes and the stream
    // keeps silent — the account is polled, not left unled: once Bybit answers, the poll sends the entry and the trade opens.
    [Test]
    public async Task An_account_whose_first_read_failed_is_polled_and_its_entry_goes()
    {
        _bench = await TraderBench.Start();
        await _bench.Pretend();
        Trade trade = await _bench.Enter(await _bench.Idea());
        _bench.Main.Unavailable = true;
        _bench.Main.MuteWhenUnavailable = true;
        await _bench.Forget();

        await _bench.Worker(trading: WorkerHost.Trading(poll: "00:00:00.3", control: "00:10:00"));
        await Poll.Until("the account is polled", async () => await _bench.Feed() == "poll");
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.Null);
        _bench.Main.Unavailable = false;

        await _bench.Until(trade.Id, TradeState.Open);
        await Poll.Until("the account is on the stream", async () => await _bench.Feed() == "stream");
    }

    // 12.27 (R8): the stop of the exchange fills worse than its level by the slippage, and the outcome counts the fill.
    [Test]
    public async Task A_stop_filled_with_slippage_closes_the_trade_with_the_outcome_of_the_fill()
    {
        _bench = await TraderBench.Start();
        _bench.Bybit.StopSlippage = 0.002m;
        await _bench.Worker();
        Trade trade = await _bench.Opened();

        _bench.Bybit.Price("SOLUSDT", 231m);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        decimal exit = 231.38m * (1 - 0.002m);
        Assert.That((closed.CloseReason, closed.Numbers.ExitPrice), Is.EqualTo(((TradeCloseReason?)TradeCloseReason.Stop, (decimal?)exit)));
        Assert.That(closed.Result, Is.EqualTo((exit - 237.40m) * 0.4m - closed.Numbers.Fees));
    }

    // 12.27 (R8): with the slippage, a stop that loses more than its level makes the loss of the day pass the limit, and the
    // emergency stop closes the other trade.
    [Test]
    public async Task A_stop_filled_with_slippage_sets_the_emergency_stop_off()
    {
        _bench = await TraderBench.Start(maxDayLoss: 2.6m);
        _bench.Bybit.StopSlippage = 0.01m;
        await _bench.Worker();
        Trade sol = await _bench.Opened();
        Trade xrp = await _bench.Enter(await _bench.Idea("XRPUSDT", ProfitFox.Domain.ValueObjects.Graph.Enums.EntrySide.Short, 1.4051, null, null));
        await _bench.Until(xrp.Id, TradeState.Open);

        _bench.Bybit.Price("SOLUSDT", 231m);

        Assert.That((await _bench.Until(sol.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Stop));
        Assert.That((await _bench.Until(xrp.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Halt));
        Assert.That((await _bench.Halt()).Loss, Is.GreaterThanOrEqualTo(2.6m));
    }

    // 12.2 (finding 2): an entry tried before the stop went off may be at the exchange — the Worker asks, and an entry found
    // there opens its trade, which the stop closes.
    [Test]
    public async Task An_entry_tried_before_the_stop_and_found_at_the_exchange_opens_and_closes_by_the_stop()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Silent = 4;
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));
        _bench.Main.Answer("order-create", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Temporary, "10016", "Server error"), 3).ToArray());
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:30", poll: "00:00:30", control: "00:10:00"));
        Trade trade = await _bench.Enter(await _bench.Idea());
        await Poll.Until("the entry was tried", () => Task.FromResult(_bench.CallsOf("order-create") == 1));
        await Poll.Until("the attempt is written", async () => (await _bench.Orders(trade.Id)).Single().Attempts == 1);

        await _bench.Api.Services.GetRequiredService<ITradeHaltRepository>().HaltAsync("trader-1", 52.10m, 50m, DateTimeOffset.UtcNow, CancellationToken.None);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Halt));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.Null, "no position is left unled");
    }

    // 12.3 (finding 3): a close the exchange filled in half and cancelled gets a close of the rest with the same reason.
    [Test]
    public async Task A_close_filled_in_part_and_cancelled_gets_a_close_of_the_rest()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Main.PartialClose = 0.5m;

        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Manual));
        Assert.That(_bench.Bybit.OrdersOf(TradeBench.MainKey).Where(order => order.Request.ReduceOnly).Select(order => order.Request.Quantity), Is.EqualTo(new[] { 0.4m, 0.2m }));
    }

    // 12.4 (finding 4): a cursor older than the seven days of Bybit is not read from: the fills are read from the earliest
    // trade of the account.
    [Test]
    public async Task A_cursor_older_than_the_window_of_Bybit_reads_from_the_earliest_trade()
    {
        _bench = await TraderBench.Start();
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE exchange_accounts SET fills_cursor_at = now() - interval '10 days'");
        }

        await _bench.Worker();
        Trade trade = await _bench.Opened();

        Assert.That(_bench.Main.FillsSince.ToList(), Has.All.GreaterThanOrEqualTo(trade.CreatedAt.AddMinutes(-2)));
    }

    // 12.5 (finding 5): before each send the price is read anew — an entry whose stop the price passed is given up.
    [Test]
    public async Task An_entry_whose_stop_the_price_passed_before_its_repeat_is_given_up()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "10016", "Server error"));
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:01"));
        Trade trade = await _bench.Enter(await _bench.Idea());
        await Poll.Until("the entry was tried", () => Task.FromResult(_bench.CallsOf("order-create") == 1));

        _bench.Bybit.Price("SOLUSDT", 231m);

        Trade cancelled = await _bench.Until(trade.Id, TradeState.Cancelled);
        Assert.That(cancelled.Reason, Is.EqualTo("вход отменён: цена 231 прошла стоп сделки 231.38"));
        Assert.That(_bench.CallsOf("order-create"), Is.EqualTo(1));
    }

    // 12.5 (finding 5): and an entry nearer the end of its idea than the margin now is given up before its first send.
    [Test]
    public async Task An_entry_nearer_the_end_than_the_margin_saved_after_the_click_is_given_up()
    {
        _bench = await TraderBench.Start();
        await _bench.Pretend();
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromMinutes(3)));
        await _bench.Api.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(TradeBench.Limits with { ExpiryMargin = TimeSpan.FromMinutes(5) }, CancellationToken.None);
        await _bench.Forget();

        await _bench.Worker();

        Assert.That((await _bench.Until(trade.Id, TradeState.Cancelled)).Reason, Is.EqualTo("вход отменён: до срока идеи меньше запаса"));
        Assert.That(_bench.CallsOf("order-create"), Is.Zero);
    }

    // 12.6 (finding 6): one empty snapshot does not close the trade: the position is read again and is still there.
    [Test]
    public async Task One_empty_snapshot_with_the_position_still_there_does_not_close_the_trade()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:10:00"));
        Trade trade = await _bench.Opened();

        _bench.Bybit.Tell(TradeBench.MainKey, ExchangePosition.Empty("SOLUSDT"));
        await Poll.Until("the trade saw an empty position", async () => (await _bench.Read(trade.Id)).FlatSince is not null);
        await Poll.Until("the position was read again", async () => (await _bench.Read(trade.Id)).FlatSince is null);

        Assert.That((await _bench.Read(trade.Id)).State, Is.EqualTo(TradeState.Open));
        Assert.That(_bench.CallsOf("closed-pnl"), Is.Zero);
    }

    // 12.8 (finding 8): the stop stays on the exchange while the trade closes.
    [Test]
    public async Task A_stop_removed_while_the_close_is_repeated_is_put_back()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:01"));
        Trade trade = await _bench.Opened();
        _bench.Main.Answer("order-create", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Temporary, "10016", "Server error"), 100).ToArray());
        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));
        await Poll.Until("the close is repeated", () => Task.FromResult(_bench.CallsOf("order-create") >= 2));

        _bench.Bybit.StopByHand(TradeBench.MainKey, "SOLUSDT", null);

        await Poll.Until("the stop is back", () => Task.FromResult(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Stop == 231.38m));
        Assert.That((await _bench.Read(trade.Id)).State, Is.EqualTo(TradeState.Closing));
    }

    // 12.9 (finding 9): the prices that do not answer do not keep the stop from going off: the loss counts by the results of
    // the positions Bybit told.
    [Test]
    public async Task Without_prices_the_loss_of_the_day_counts_by_the_positions_and_the_stop_goes_off()
    {
        _bench = await TraderBench.Start(maxDayLoss: 1m);
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:00:00.3"));
        Trade trade = await _bench.Opened();
        _bench.Bybit.MarketUnavailable = true;
        _bench.Bybit.PricesLost(true);

        _bench.Bybit.Price("SOLUSDT", 233m);

        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Halt));
    }

    // 12.11 (finding 11): the fills of the stream do not move the cursor; only a read of REST does.
    [Test]
    public async Task The_fills_of_the_stream_do_not_move_the_cursor()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:10:00"));
        await _bench.Opened();
        await Task.Delay(TimeSpan.FromSeconds(0.3));

        Assert.That((await _bench.Api.Services.GetRequiredService<IExchangeAccountRepository>().FindAsync(_bench.Account.Id, CancellationToken.None))!.FillsCursorAt, Is.Null);
    }

    // 12.15 (N1): the protective stop at the limit is X % from the price of the click less a step; a price a step better
    // for the trade before the send is no reason to give the entry up.
    [TestCase("XRPUSDT", 1.4126, 1.4125)]
    [TestCase("SOLUSDT", 237.40, 237.41)]
    public async Task An_entry_whose_price_moved_a_step_its_way_goes(string symbol, double click, double now)
    {
        _bench = await TraderBench.Start();
        await _bench.Pretend();
        Trade trade = await _bench.Enter(symbol == "XRPUSDT"
            ? await _bench.Idea("XRPUSDT", ProfitFox.Domain.ValueObjects.Graph.Enums.EntrySide.Short, click, null, null)
            : await _bench.Idea(stop: null, target: null));
        Assert.That(trade.StopSource, Is.EqualTo(StopSource.Limit));
        _bench.Bybit.Price(symbol, (decimal)now);
        await _bench.Forget();

        await _bench.Worker();

        Assert.That((await _bench.Until(trade.Id, TradeState.Open)).Numbers.Entered, Is.EqualTo(trade.Quantity));
    }

    // 12.15 (N1): a price gone further its way than the drift allowed gives the entry up.
    [Test]
    public async Task An_entry_whose_price_moved_its_way_beyond_the_drift_is_given_up()
    {
        _bench = await TraderBench.Start();
        await _bench.Pretend();
        Trade trade = await _bench.Enter(await _bench.Idea(stop: null, target: null));
        _bench.Bybit.Price("SOLUSDT", 238.80m);
        await _bench.Forget();

        await _bench.Worker();

        Assert.That((await _bench.Until(trade.Id, TradeState.Cancelled)).Reason, Does.StartWith("вход отменён: цена 238.8 ушла от цены клика 237.4"));
        Assert.That(_bench.CallsOf("order-create"), Is.Zero);
    }

    // 12.15 (N1): at 10× with a limit of 4.9 % a move of 0.2 % its way is within the drift and past half the way to the
    // liquidation from the price now.
    [Test]
    public async Task An_entry_whose_stop_is_no_longer_nearer_than_half_the_way_to_the_liquidation_is_given_up()
    {
        _bench = await TraderBench.Start();
        await _bench.Api.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(
            TradeBench.Limits with { Leverage = 10, MaxLossPercent = 4.9m, ExpiryMargin = TimeSpan.Zero }, CancellationToken.None);
        await _bench.Pretend();
        Trade trade = await _bench.Enter(await _bench.Idea(stop: null, target: null));
        _bench.Bybit.Price("SOLUSDT", 237.87m);
        await _bench.Forget();

        await _bench.Worker();

        Assert.That((await _bench.Until(trade.Id, TradeState.Cancelled)).Reason, Does.Contain("половины пути до ликвидации"));
    }

    // 12.17 (N3): the last close is filled, and the position is still there with what the trade holds — the rest is closed.
    [Test]
    public async Task A_close_filled_with_the_position_still_there_gets_a_close_of_the_rest()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Main.PartialClose = 0.5m;
        _bench.Main.PartialReportsFilled = true;

        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));

        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Manual));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.Null);
    }

    // 12.18 (N4): an entry found by its position alone, its fills not read yet, is not left unled: past CloseWait the trade
    // opens by the snapshot of the position and closes at its end as any other.
    [Test]
    public async Task An_entry_found_by_its_position_alone_opens_by_the_snapshot_and_is_led()
    {
        _bench = await TraderBench.Start(entryWait: TimeSpan.FromSeconds(1));
        _bench.Main.Silent = 4;
        _bench.Main.HistoryLags = true;
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:30", control: "00:10:00"));
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromSeconds(6)));
        await Poll.Until("the entry was tried", () => Task.FromResult(_bench.CallsOf("order-create") == 1));
        _bench.Bybit.HideFills(TradeBench.MainKey, "SOLUSDT", ProfitFox.Domain.ValueObjects.Trades.Enums.TradeSide.Long);

        Trade open = await _bench.Until(trade.Id, TradeState.Open);
        Assert.That((open.Numbers.Held, open.Numbers.EntryPrice), Is.EqualTo((0.4m, (decimal?)237.40m)));
        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Time));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.Null);
    }

    // 12.20 (R1): Bybit no longer takes the key — «Разобрана» is refused with the way out, and the owner's confirmation
    // resolves the trade without the check.
    [Test]
    public async Task A_trade_in_error_of_a_key_Bybit_no_longer_takes_is_resolved_only_with_the_confirmation()
    {
        _bench = await TraderBench.Start();
        WorkerHost worker = await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", ProfitFox.Domain.ValueObjects.Trades.Enums.TradeSide.Long, 0.1m);
        await _bench.Until(trade.Id, TradeState.Error);
        await _bench.Stop(worker);
        _bench.Main.Refusal = () => throw new ProfitFox.Application.Exceptions.ExchangeKeyRejectedException("API key is invalid.");

        Assert.ThrowsAsync<ProfitFox.Application.Exceptions.TradeUncheckedException>(() => _bench.Sender.Send(new ResolveTradeCommand(trade.Id)));
        Trade resolved = await _bench.Sender.Send(new ResolveTradeCommand(trade.Id, Unchecked: true));

        Assert.That((resolved.State, resolved.CloseReason, resolved.Reason), Is.EqualTo((TradeState.Closed, (TradeCloseReason?)TradeCloseReason.Exchange, "разобрана без проверки на бирже: Bybit не принимает ключ аккаунта")));
    }

    // 12.20 (R1): the secret does not read — the same way out; and the confirmation does not skip a check that is possible.
    [Test]
    public async Task A_trade_in_error_of_an_unreadable_secret_is_resolved_with_the_confirmation_and_a_possible_check_is_not_skipped()
    {
        _bench = await TraderBench.Start();
        WorkerHost worker = await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", ProfitFox.Domain.ValueObjects.Trades.Enums.TradeSide.Long, 0.1m);
        await _bench.Until(trade.Id, TradeState.Error);
        await _bench.Stop(worker);

        Assert.ThrowsAsync<ProfitFox.Application.Exceptions.TradeConflictException>(() => _bench.Sender.Send(new ResolveTradeCommand(trade.Id, Unchecked: true)), "the position is there and can be read");
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE exchange_accounts SET secret_tag = '\\x00000000000000000000000000000000'");
        }

        Assert.ThrowsAsync<ProfitFox.Application.Exceptions.TradeUncheckedException>(() => _bench.Sender.Send(new ResolveTradeCommand(trade.Id)));
        Assert.That((await _bench.Sender.Send(new ResolveTradeCommand(trade.Id, Unchecked: true))).Reason, Is.EqualTo("разобрана без проверки на бирже: секрет аккаунта не читается"));
    }

    // 12.26 (R7): a drift meant as percent and an interval of nought keep the Worker from starting.
    [TestCase("Trading:EntryDrift", "0.5", "not percent")]
    [TestCase("Trading:PollInterval", "00:00:00", "Trading:PollInterval must be above nought")]
    public async Task Settings_of_the_trading_Worker_out_of_their_range_keep_it_from_starting(string key, string value, string message)
    {
        _bench = await TraderBench.Start();
        var trading = new Dictionary<string, string?>(WorkerHost.FastTrading) { [key] = value };

        InvalidOperationException? refused = Assert.ThrowsAsync<InvalidOperationException>(() => WorkerHost.Start(_bench.Server, "trader", runs: false, bybit: _bench.Bybit, trading: trading));

        Assert.That(refused!.Message, Does.Contain(message));
    }

    // 12.13 (finding 13): the answer is lost and the stream keeps silent — one more send, one order, one position.
    [Test]
    public async Task A_lost_answer_with_a_silent_stream_sends_twice_and_makes_one_order_and_one_position()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Silent = 4;
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:10:00"));

        Trade trade = await _bench.Enter(await _bench.Idea());

        Assert.That((await _bench.Until(trade.Id, TradeState.Open, TimeSpan.FromSeconds(10))).Numbers.Held, Is.EqualTo(0.4m));
        Assert.That(_bench.CallsOf("order-create"), Is.EqualTo(2));
        Assert.That(_bench.Bybit.OrdersOf(TradeBench.MainKey), Has.Count.EqualTo(1));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Size, Is.EqualTo(0.4m));
    }

    // 12.14 (finding 14): the history of orders lags — past the wait the fills and the position show the entry, and the trade
    // opens instead of being cancelled.
    [Test]
    public async Task An_entry_the_history_does_not_show_yet_is_found_by_its_fills_and_opens()
    {
        _bench = await TraderBench.Start(entryWait: TimeSpan.FromSeconds(1));
        _bench.Main.Silent = 4;
        _bench.Main.HistoryLags = true;
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));
        _bench.Main.Answer("order-create", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Temporary, "10016", "Server error"), 100).ToArray());
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:30", poll: "00:00:30", control: "00:10:00"));

        Trade trade = await _bench.Enter(await _bench.Idea());

        Assert.That((await _bench.Until(trade.Id, TradeState.Open)).Numbers.Entered, Is.EqualTo(0.4m));
    }
}
