using ProfitFox.Application.Ports.Exchange;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;
using ProfitFox.Tests.Integration.Application.Commands.Trades;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices.Trades;

/// <summary>
///     Change take-idea-trades, task 7.5 (design.md, decision 4; spec idea-trades «Плечо сделки», «Заявка записана до
///     отправки», «Вход не ждёт», «Стоп и цель на бирже»): the leverage, the entry with its stop and target, the answers of the
///     table, the repeats, and the wait for the entry the trade got at the click — on <see cref="Exchange.FakeBybit" />.
/// </summary>
[NonParallelizable]
public sealed class TradeOrdersTests
{
    private TraderBench _bench = null!;

    [TearDown]
    public async Task Stop()
    {
        await _bench.DisposeAsync();
    }

    // Spec «Плечо 3×».
    [Test]
    public async Task The_leverage_of_the_trade_is_set_before_the_entry()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();

        await _bench.Opened();

        string[] calls = [.. _bench.Bybit.CallsNow().Where(call => call.Call is "set-leverage" or "order-create").Select(call => call.Call)];
        Assert.That(calls, Is.EqualTo(new[] { "set-leverage", "order-create" }));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Leverage, Is.EqualTo(3));
    }

    // Spec «Плечо уже такое».
    [Test]
    public async Task A_leverage_that_is_already_that_is_no_error()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Answer("set-leverage", new ExchangeAnswer(ExchangeAnswerKind.Unchanged, "110043", "leverage not modified"));
        await _bench.Worker();

        Trade trade = await _bench.Opened();

        Assert.That(trade.State, Is.EqualTo(TradeState.Open));
    }

    // Spec «Ответ потерялся».
    [Test]
    public async Task A_lost_answer_sends_the_same_order_again_and_the_exchange_has_one_order_and_one_position()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));
        await _bench.Worker();

        Trade trade = await _bench.Opened();

        Assert.That(_bench.CallsOf("order-create"), Is.GreaterThanOrEqualTo(1));
        Assert.That(_bench.Bybit.OrdersOf(TradeBench.MainKey), Has.Count.EqualTo(1));
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT")!.Size, Is.EqualTo(0.4m));
        Assert.That((await _bench.Read(trade.Id)).Numbers.Held, Is.EqualTo(0.4m));
    }

    // Spec «Падение между записью и отправкой»: the order written while no Worker worked is sent after the start.
    [Test]
    public async Task An_order_written_while_no_Worker_worked_is_sent_after_the_start()
    {
        _bench = await TraderBench.Start();
        await _bench.Pretend();

        Trade trade = await _bench.Enter(await _bench.Idea());
        Assert.That(_bench.CallsOf("order-create"), Is.Zero);
        await _bench.Forget();
        await _bench.Worker();

        Assert.That((await _bench.Until(trade.Id, TradeState.Open)).Numbers.Entered, Is.EqualTo(0.4m));
    }

    // Spec «Bybit не отвечал минуту»: past the wait of the trade the entry is cancelled and does not go later.
    [Test]
    public async Task An_entry_Bybit_does_not_take_within_the_wait_is_cancelled_and_not_sent_after()
    {
        _bench = await TraderBench.Start(entryWait: TimeSpan.FromSeconds(1.5));
        _bench.Main.Answer("order-create", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Temporary, "10016", "Server error"), 100).ToArray());
        await _bench.Worker();

        Trade trade = await _bench.Enter(await _bench.Idea());
        Trade cancelled = await _bench.Until(trade.Id, TradeState.Cancelled);
        int sent = _bench.CallsOf("order-create");
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.That(cancelled.Reason, Is.EqualTo("вход не ушёл вовремя"));
        Assert.That(_bench.CallsOf("order-create"), Is.EqualTo(sent), "nothing goes after the cancel");
        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.Null);
    }

    // Spec «Заявка всё же дошла»: the wait is over, and the exchange has the order of the trade filled — the trade opens.
    [Test]
    public async Task An_entry_found_filled_at_the_exchange_after_the_wait_opens_the_trade()
    {
        _bench = await TraderBench.Start(entryWait: TimeSpan.FromSeconds(1));
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Temporary, "lost"));
        _bench.Main.Answer("order-create", Enumerable.Repeat(new ExchangeAnswer(ExchangeAnswerKind.Temporary, "10016", "Server error"), 100).ToArray());
        _bench.Main.Silent = 100;
        await _bench.Worker(trading: WorkerHost.Trading(backoff: "00:00:05", poll: "00:00:05", control: "00:10:00"));

        Trade trade = await _bench.Enter(await _bench.Idea());

        Trade open = await _bench.Until(trade.Id, TradeState.Open);
        Assert.That(open.Numbers.Entered, Is.EqualTo(0.4m));
    }

    // Spec «Стенд не работает»: the stop is on the exchange and fills without the bench.
    [Test]
    public async Task The_stop_fills_at_the_exchange_while_no_Worker_works()
    {
        _bench = await TraderBench.Start();
        WorkerHost worker = await _bench.Worker();
        await _bench.Opened();
        await _bench.Stop(worker);

        _bench.Bybit.Price("SOLUSDT", 230m);

        Assert.That(_bench.Bybit.PositionOf(TradeBench.MainKey, "SOLUSDT"), Is.Null);
        Assert.That(_bench.Bybit.FillsOf(TradeBench.MainKey)[^1].StopType, Is.EqualTo(ExchangeStopType.StopLoss));
    }

    // Spec «Сделка без цели»: the entry carries only the stop.
    [Test]
    public async Task A_trade_without_a_target_sends_only_the_stop()
    {
        _bench = await TraderBench.Start();
        await _bench.Worker();

        await _bench.Opened(target: null);

        (ExchangeOrderRequest request, _) = _bench.Bybit.OrdersOf(TradeBench.MainKey).Single();
        Assert.That((request.Stop, request.Target, request.ReduceOnly), Is.EqualTo(((decimal?)231.38m, (decimal?)null, false)));
    }

    [Test]
    public async Task An_entry_Bybit_refuses_cancels_the_trade_with_the_reason_of_Bybit()
    {
        _bench = await TraderBench.Start();
        _bench.Main.Answer("order-create", new ExchangeAnswer(ExchangeAnswerKind.Refused, "110007", "ab not enough for new order"));
        await _bench.Worker();

        Trade trade = await _bench.Enter(await _bench.Idea());

        Assert.That((await _bench.Until(trade.Id, TradeState.Cancelled)).Reason, Is.EqualTo("Bybit: ab not enough for new order"));
    }
}
