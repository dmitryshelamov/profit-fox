using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Commands.Trades.CloseTrade;
using ProfitFox.Application.Commands.Trades.ResolveTrade;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Integration.Application.Commands.Trades;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices.Trades;

/// <summary>
///     Change take-idea-trades, tasks 7.2, 7.6, 7.7 and 7.8 (design.md, decisions 3, 5, 8 and 10): one handler for the stream
///     and REST, the check of the position against the trade, the guard of the stop, and the close by the end, by the button
///     and by «Разобрана» — on <see cref="Exchange.FakeBybit" />.
/// </summary>
[NonParallelizable]
public sealed class TradeFollowTests
{
    private TraderBench _bench = null!;

    [TearDown]
    public async Task Stop()
    {
        await _bench.DisposeAsync();
    }

    // 7.2, spec «Исполнение пришло дважды»: by the stream twice and by REST — one fill, the outcome does not change.
    [Test]
    public async Task A_fill_told_twice_and_by_both_paths_counts_once()
    {
        _bench = await TraderBench.Start();
        _bench.Main.FillTwice = true;
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:00:00.5"));

        Trade trade = await _bench.Opened();
        await Task.Delay(TimeSpan.FromSeconds(1.2));

        Assert.That((await _bench.Trades.FillsAsync(trade.Id, CancellationToken.None)).Count, Is.EqualTo(1));
        Assert.That((await _bench.Read(trade.Id)).Numbers.Held, Is.EqualTo(0.4m));
    }

    // 7.2, spec «Исполнение раньше заявки».
    [Test]
    public async Task A_fill_before_its_order_opens_the_trade_and_the_order_does_not_go_back()
    {
        _bench = await TraderBench.Start();
        _bench.Main.FillBeforeOrder = true;
        await _bench.Worker();

        Trade trade = await _bench.Opened();

        await Poll.Until("the entry order is filled", async () => (await _bench.Orders(trade.Id)).Single().Status == TradeOrderStatus.Filled);
        Assert.That((await _bench.Read(trade.Id)).Numbers.Entered, Is.EqualTo(0.4m));
    }

    // 7.6, spec «Закрытие замечено» by REST without the stream.
    [Test]
    public async Task A_close_by_the_target_is_seen_by_the_poll_within_the_interval()
    {
        _bench = await TraderBench.Start();
        _bench.Main.StreamRefusal = "no socket";
        await _bench.Worker();
        Trade trade = await _bench.Opened();

        _bench.Bybit.Price("SOLUSDT", 246m);

        TimeSpan took = await Poll.Until("the trade is closed", async () => (await _bench.Read(trade.Id)).State == TradeState.Closed);
        Assert.That((await _bench.Read(trade.Id)).CloseReason, Is.EqualTo(TradeCloseReason.Target));
        Assert.That(took, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    // 7.6, spec «Позицию увеличили руками»: «Ошибка», the stop stays, the end does not close it.
    [Test]
    public async Task A_position_made_bigger_by_hand_puts_the_trade_in_error_and_the_bench_leaves_it()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();

        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", TradeSide.Long, 0.1m);

        Trade error = await _bench.Until(trade.Id, TradeState.Error);
        Assert.That(error.Reason, Does.StartWith("позиция на бирже больше сделки"));
        _bench.Bybit.StopByHand(TradeBench.MainKey, "SOLUSDT", null);
        await Task.Delay(TimeSpan.FromSeconds(0.5));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Stop, Is.Null, "the stop of a trade in error is not touched");
        Assert.That(_bench.CallsOf("trading-stop"), Is.Zero);
    }

    // 7.6, spec «Часть закрыли руками»: half sold by hand — the trade stays open with half, and the end closes the rest.
    [Test]
    public async Task Half_closed_by_hand_leaves_the_trade_open_with_half_and_the_end_closes_the_rest()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromSeconds(4)));
        await _bench.Until(trade.Id, TradeState.Open);

        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", TradeSide.Short, 0.2m);
        await Poll.Until("half is held", async () => (await _bench.Read(trade.Id)).Numbers.Held == 0.2m);
        Assert.That((await _bench.Read(trade.Id)).State, Is.EqualTo(TradeState.Open));

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Time));
        Assert.That(_bench.Bybit.OrdersOf(TradeBench.MainKey).Last().Request.Quantity, Is.EqualTo(0.2m));
    }

    // 7.6, spec «Идея вышла по стопу в журнале»: the outcome of the idea does not close the trade.
    [Test]
    public async Task The_idea_closed_by_its_stop_in_the_journal_leaves_the_trade_open()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        IIdeaRepository ideas = _bench.Api.Services.GetRequiredService<IIdeaRepository>();
        Idea idea = (await ideas.FindAsync(trade.IdeaId, CancellationToken.None))!;
        idea.State = IdeaState.Closed;
        idea.ExitTime = DateTimeOffset.UtcNow;
        idea.ExitReason = ProfitFox.Domain.ValueObjects.Trades.Enums.ExitReason.Stop;
        idea.Result = -0.02;
        await ideas.SaveAsync(idea, CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(0.5));

        Assert.That((await _bench.Read(trade.Id)).State, Is.EqualTo(TradeState.Open));
    }

    // 7.6: an empty position without its fills reads the fills again and closes by the closed result of Bybit after CloseWait.
    [Test]
    public async Task An_empty_position_without_fills_closes_by_the_closed_result_after_the_wait()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:10:00"));
        Trade trade = await _bench.Opened();
        _bench.Main.Silent = 1;
        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", TradeSide.Short, 0.4m);
        // The fill of the hand is not in the reads either: Bybit kept it out of its window.
        _bench.Bybit.HideFills(TradeBench.MainKey, "SOLUSDT", TradeSide.Short);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);

        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Exchange));
        Assert.That(_bench.CallsOf("closed-pnl"), Is.EqualTo(1));
        Assert.That(closed.Result, Is.Not.Null);
    }

    // 7.7, spec trade-risk «Стоп сняли руками» — by the stream, and by the snapshot of REST.
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_stop_removed_by_hand_is_put_back(bool rest)
    {
        _bench = await TraderBench.Start();
        if (rest)
        {
            _bench.Main.StreamRefusal = "no socket";
        }

        await _bench.Worker();
        await _bench.Opened();

        _bench.Bybit.StopByHand(TradeBench.MainKey, "SOLUSDT", null);

        await Poll.Until("the stop is back", () => Task.FromResult(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Stop == 231.38m));
    }

    // 7.7, spec trade-risk «Стоп не ставится».
    [Test]
    public async Task A_stop_Bybit_refuses_as_many_times_as_the_attempts_closes_the_trade_without_a_stop()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Answer("trading-stop", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Refused, "10001", "params error"), 10).ToArray());
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:00:00.3"));
        Trade trade = await _bench.Opened();

        _bench.Bybit.StopByHand(TradeBench.MainKey, "SOLUSDT", null);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.NoStop));
        Assert.That(_bench.CallsOf("trading-stop"), Is.EqualTo(3));
    }

    // 7.7, spec trade-risk «Стоп подвинули ближе».
    [Test]
    public async Task A_stop_moved_nearer_by_hand_is_left()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:00:00.3"));
        await _bench.Opened();

        _bench.Bybit.StopByHand(TradeBench.MainKey, "SOLUSDT", 235m);
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Stop, Is.EqualTo(235m));
        Assert.That(_bench.CallsOf("trading-stop"), Is.Zero);
    }

    // 7.7, spec trade-risk «Сделка живёт со своими числами»: five attempts saved after the click leave the trade three.
    [Test]
    public async Task A_trade_keeps_the_attempts_it_got_at_the_click()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Answer("trading-stop", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Refused, "10001", "params error"), 10).ToArray());
        await _bench.Worker(trading: WorkerHost.Trading(control: "00:00:00.3"));
        Trade trade = await _bench.Opened();
        await _bench.Api.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(TradeBench.Limits with { StopAttempts = 5 }, CancellationToken.None);

        _bench.Bybit.StopByHand(TradeBench.MainKey, "SOLUSDT", null);

        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.NoStop));
        Assert.That(_bench.CallsOf("trading-stop"), Is.EqualTo(3));
    }

    // 7.8, spec «Срок вышел».
    [Test]
    public async Task At_its_end_the_trade_closes_by_the_market_by_the_time()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromSeconds(2)));
        await _bench.Until(trade.Id, TradeState.Open);

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);

        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Time));
        Assert.That(closed.ClosedAt, Is.GreaterThanOrEqualTo(trade.Until));
        Assert.That(_bench.Bybit.OrdersOf(TradeBench.MainKey).Last().Request.ReduceOnly, Is.True);
    }

    // 7.8, spec «Стенд проспал срок».
    [Test]
    public async Task A_trade_whose_end_passed_while_no_Worker_worked_closes_by_the_time_at_the_start()
    {
        _bench = await TraderBench.Start();
        WorkerHost worker = await _bench.Worker();
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromSeconds(2)));
        await _bench.Until(trade.Id, TradeState.Open);
        await _bench.Stop(worker);
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Assert.That((await _bench.Read(trade.Id)).State, Is.EqualTo(TradeState.Open));

        await _bench.Worker("trader-2");

        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Time));
    }

    // 7.8, spec «Режим сменили при открытой сделке».
    [Test]
    public async Task A_margin_mode_changed_with_an_open_trade_leaves_it_open_with_its_stop_and_the_end_closes_it()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Enter(await _bench.Idea(left: TimeSpan.FromSeconds(3)));
        await _bench.Until(trade.Id, TradeState.Open);

        _bench.Main.MarginMode = "REGULAR_MARGIN";

        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Time));
        Assert.ThrowsAsync<TradeEntryRefusedException>(() => _bench.Enter(_bench.Idea().Result));
    }

    // 7.8, spec «Закрыть по рынку».
    [Test]
    public async Task The_button_closes_the_trade_by_the_market_by_hand()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();

        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));

        Trade closed = await _bench.Until(trade.Id, TradeState.Closed);
        Assert.That(closed.CloseReason, Is.EqualTo(TradeCloseReason.Manual));
    }

    // 7.8, spec «Сделка ещё ждёт входа».
    [Test]
    public async Task A_trade_still_waiting_for_its_entry_has_nothing_to_close()
    {
        _bench = await TraderBench.Start();
        await _bench.Pretend();

        Trade trade = await _bench.Enter(await _bench.Idea());
        Assert.ThrowsAsync<TradeConflictException>(() => _bench.Sender.Send(new CloseTradeCommand(trade.Id)));
    }

    // 7.8: a close Bybit refuses leaves the trade open with the reason, and the close can be tried again.
    [Test]
    public async Task A_close_Bybit_refuses_leaves_the_trade_open_with_the_reason()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Refused, "110017", "reduce-only order has same side with current position"));

        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));
        await Poll.Until("the close is refused", async () => (await _bench.Read(trade.Id)).Reason is not null);

        Trade open = await _bench.Read(trade.Id);
        Assert.That((open.State, open.Reason), Is.EqualTo((TradeState.Open, "Bybit: reduce-only order has same side with current position")));
        await _bench.Sender.Send(new CloseTradeCommand(trade.Id));
        Assert.That((await _bench.Until(trade.Id, TradeState.Closed)).CloseReason, Is.EqualTo(TradeCloseReason.Manual));
    }

    // 7.8, spec «Позиция ещё есть» and «Позиции нет».
    [Test]
    public async Task A_trade_in_error_is_resolved_only_once_its_position_is_gone()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();
        Trade trade = await _bench.Opened();
        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", TradeSide.Long, 0.1m);
        await _bench.Until(trade.Id, TradeState.Error);

        TradeConflictException? refused = Assert.ThrowsAsync<TradeConflictException>(() => _bench.Sender.Send(new ResolveTradeCommand(trade.Id)));
        Assert.That(refused!.Message, Is.EqualTo("Сначала закройте позицию на Bybit."));

        _bench.Bybit.ByHand(TradeBench.MainKey, "SOLUSDT", TradeSide.Short, 0.5m);
        Trade closed = await _bench.Sender.Send(new ResolveTradeCommand(trade.Id));

        Assert.That((closed.State, closed.CloseReason), Is.EqualTo((TradeState.Closed, (TradeCloseReason?)TradeCloseReason.Exchange)));
        Assert.That((await _bench.Enter(await _bench.Idea())).State, Is.EqualTo(TradeState.Pending), "the pair may be entered again");
    }
}
