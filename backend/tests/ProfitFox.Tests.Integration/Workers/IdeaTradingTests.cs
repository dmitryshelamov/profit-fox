using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Integration.Infrastructure;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>A trading Worker keeps trades by ideas on the exchange: one entry with a stop and a target the exchange stands.</summary>
public class IdeaTradingTests
{
    private LiveTradingScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scenario = await LiveTradingScenario.CreateAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scenario.DisposeAsync();
    }

    // Scenarios "Вход по рынку на linear" and "Цель".
    [Test]
    public async Task Target_closes_the_trade_with_the_result_after_the_fees()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Orders.Single().ShouldBe(new OrderRequest(entry, OrderSide.Buy, OrderType.Market, null, 0.47m, null, new Protection(115m, 100m)));
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");
        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        open.HeldQuantity.ShouldBe(0.47m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");

        _scenario.Exchange.TriggerChild(entry, StopOrderKind.TakeProfit, 115m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtTarget, "the target");
        IdeaTrade closed = await _scenario.TradeAsync(trade.Id);
        closed.Result.ShouldBe((0.47m * 115m) - (0.47m * 105m) - 0.02m);
        closed.ResultR.ShouldBe(closed.Result / 2m);
        (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status.ShouldBe(LiveOrderStatus.Filled);
    }

    // Scenario "Откат не дождался": the limit of the pullback is cancelled when the time of the entry of the idea is out.
    [Test]
    public async Task Pullback_that_is_not_filled_by_its_deadline_is_cancelled_without_a_result()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.Orders.Single().ShouldBe(new OrderRequest(entry, OrderSide.Buy, OrderType.Limit, 105m, 0.47m, null, new Protection(115m, 100m)));

        _scenario.Factory.Time.Advance(trade.EntryDeadline!.Value - _scenario.Factory.Time.GetUtcNow());

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Cancelled, "the cancelled trade");
        _scenario.Exchange.Cancels.ShouldBe([entry]);
        (await _scenario.TradeAsync(trade.Id)).Result.ShouldBeNull();
    }

    // Scenario "Закрыть по рынку": the close goes first while the stop and the target stand; they come off when it has taken out the position.
    [Test]
    public async Task Close_by_the_market_sells_what_is_held_and_then_takes_the_stop_and_the_target_off()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();
        string close = new IdeaTradeOrderId(trade.Id, 2).Code;
        _scenario.Exchange.Orders.Last().ShouldBe(new OrderRequest(close, OrderSide.Sell, OrderType.Market, null, 0.47m, null, ReduceOnly: true));
        _scenario.Exchange.Fill(close, 0.47m, 110m, fee: 0.03m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedManually, "the closed trade");
        (await _scenario.TradeAsync(trade.Id)).Result.ShouldBe((0.47m * 110m) - (0.47m * 105m) - 0.02m - 0.03m);
        await _scenario.WaitAsync(() => _scenario.Exchange.ChildCancels.Count == 2, "the stop and the target taken off");
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).All(child => child.Finished), "the stop and the target ended");
    }

    // Requirement "Отмена и закрытие сделки": a close the exchange cancels in part leaves the trade open, protected, for another try; the exits add up.
    [Test]
    public async Task Close_cancelled_in_part_leaves_the_trade_open_with_its_stop_and_target_and_the_next_close_finishes_it()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        string close = new IdeaTradeOrderId(trade.Id, 2).Code;

        _scenario.Exchange.Fill(close, 0.2m, 110m, fee: 0.01m);
        _scenario.Exchange.CancelOnBybit(close);

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Any(order => order.Number == 2 && order.Status == LiveOrderStatus.Cancelled), "the end of the close");
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Reason is not null, "the reason");
        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        (open.Status, open.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, 0.27m));
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 3, "the second close");
        string again = new IdeaTradeOrderId(trade.Id, 3).Code;
        _scenario.Exchange.Orders.Last().ShouldBe(new OrderRequest(again, OrderSide.Sell, OrderType.Market, null, 0.27m, null, ReduceOnly: true));
        _scenario.Exchange.Fill(again, 0.27m, 111m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedManually, "the closed trade");
        IdeaTrade closed = await _scenario.TradeAsync(trade.Id);
        closed.Manual.ShouldBe(new LegTotals(0.47m, (0.2m * 110m) + (0.27m * 111m), 0.03m));
        await _scenario.WaitAsync(() => _scenario.Exchange.ChildCancels.Count == 2, "the stop and the target taken off");
    }

    // Requirement "Отмена и закрытие сделки": a close Bybit refuses is no failure; the stop and the target stand and the owner sees why.
    [Test]
    public async Task Close_refused_for_good_leaves_the_trade_open_with_its_stop_and_target_and_the_reason()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        _scenario.Exchange.Refusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Reduce-only rejected."));

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Any(order => order.Number == 2 && order.Status == LiveOrderStatus.Rejected), "the refused close");
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Reason is not null, "the reason");
        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        (open.Status, open.CloseRequested, open.Reason!).ShouldBe((IdeaTradeStatus.Open, false, open.Reason!));
        open.Reason!.ShouldContain("Reduce-only rejected.");
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();
    }

    [Test]
    public async Task Close_rejected_on_the_stream_leaves_the_trade_open_with_its_stop_and_target_and_the_reason()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");

        _scenario.Exchange.Reject(new IdeaTradeOrderId(trade.Id, 2).Code, "Position is zero.");

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Reason is not null, "the reason");
        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        open.Status.ShouldBe(IdeaTradeStatus.Open);
        open.Reason!.ShouldContain("Position is zero.");
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();
    }

    // Requirement "Отмена и закрытие сделки": a cancel of the stop or the target that Bybit meets with a pause is asked again.
    [Test]
    public async Task Stop_and_target_that_cannot_be_taken_off_for_a_while_are_asked_again()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Transient, Reason: "Too many visits."));

        _scenario.Exchange.Fill(new IdeaTradeOrderId(trade.Id, 2).Code, 0.47m, 110m, fee: 0.03m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedManually, "the closed trade");
        await SettleAsync();
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).All(child => child.Finished), "the stop and the target ended");
        _scenario.Exchange.ChildCancels.Count.ShouldBe(3);
    }

    [Test]
    public async Task Stop_and_target_that_cannot_be_taken_off_for_good_are_named_to_the_owner()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Not allowed."));

        _scenario.Exchange.Fill(new IdeaTradeOrderId(trade.Id, 2).Code, 0.47m, 110m, fee: 0.03m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Reason is not null, "the reason");
        IdeaTrade closed = await _scenario.TradeAsync(trade.Id);
        closed.Status.ShouldBe(IdeaTradeStatus.ClosedManually);
        closed.Reason!.ShouldContain("Not allowed.");
    }

    // Scenario "Стоп сработал, пока Worker лежал": the stop stands on the exchange without the Worker, and its trade is read from the history.
    [Test]
    public async Task Stop_that_fired_while_the_worker_was_down_closes_the_trade_from_the_history()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        WorkerHost first = _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");
        await _scenario.StopWorkerAsync(first);

        _scenario.Exchange.TriggerChild(entry, StopOrderKind.StopLoss, 100m, silently: true);
        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtStop, "the stop");
        (await _scenario.TradeAsync(trade.Id)).Result.ShouldBe((0.47m * 100m) - (0.47m * 105m) - 0.02m);
        _scenario.Exchange.Sent.Count.ShouldBe(1);
    }

    // Scenario "Ордер не ставится второй раз": a restart sends nothing again and the trade goes on.
    [Test]
    public async Task Restarted_worker_does_not_send_the_entry_again()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        WorkerHost first = _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");
        await _scenario.StopWorkerAsync(first);

        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.AccountSubscribers == 1, "the trade taken again");
        _scenario.Exchange.TriggerChild(entry, StopOrderKind.TakeProfit, 115m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtTarget, "the target");
        _scenario.Exchange.Sent.Count.ShouldBe(1);
    }

    // Scenario "Стоп сработал рядом с ботом": the stop belongs to the trade, so the neighbour bot does not take it for a foreign fill.
    [Test]
    public async Task Stop_that_fires_next_to_a_bot_closes_the_trade_and_the_bot_goes_on()
    {
        Guid bot = await BotWithPositionAsync();
        IdeaTrade trade = await OpenTradeAsync(0.2m);

        _scenario.Exchange.TriggerChild(new IdeaTradeOrderId(trade.Id, 1).Code, StopOrderKind.StopLoss, 100m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtStop, "the stop");
        await SettleAsync();
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
        _scenario.Exchange.Holding.ShouldBe(1m);
    }

    // Scenario "Вход сделки по идее рядом с ботом": the entry of the trade is no foreign execution on the instrument of the bot.
    [Test]
    public async Task Entry_of_a_trade_next_to_a_bot_is_no_foreign_fill()
    {
        Guid bot = await BotWithPositionAsync();

        IdeaTrade trade = await OpenTradeAsync(0.2m);
        await SettleAsync();

        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Open);
    }

    // Scenario "Шорт-бот рядом с лонг-сделкой".
    [Test]
    public async Task Bot_of_the_opposite_direction_does_not_start_next_to_an_open_trade()
    {
        Guid bot = await _scenario.CreateBotAsync("ema-short", category: "linear");
        await OpenTradeAsync(0.2m);

        HttpResponseMessage response = await _scenario.Client.PostAsync($"/api/bots/{bot}/start", null);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("opposite");
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Stopped);
    }

    // Scenario "Денег не хватает с учётом сделки по идее": 1000 free, 199.5 for the entry that waits, 900 for the bot.
    [Test]
    public async Task Entry_that_waits_takes_its_amount_from_the_money_of_a_starting_bot()
    {
        _scenario.Factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(1000m);
        await _scenario.CreateTradeAsync(quantity: 1.9m);
        Guid bot = await _scenario.CreateBotAsync(balance: 900m, category: "linear");

        HttpResponseMessage response = await _scenario.Client.PostAsync($"/api/bots/{bot}/start", null);

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Not enough free money");
    }

    // Scenario "Позицию закрыли руками": the trade fails, and its stop and target stay on the exchange.
    [Test]
    public async Task Position_closed_by_hand_fails_the_trade_and_leaves_its_stop_and_target()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        WorkerHost first = _scenario.Workers.Single();
        await _scenario.StopWorkerAsync(first);
        _scenario.Exchange.Holding = 0m;

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        (await _scenario.TradeAsync(trade.Id)).Reason!.ShouldContain("Exchange position 0 does not match the positions of the bots and the idea trades 0.47");
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();
        _scenario.Exchange.ChildrenOf(entry).ShouldAllBe(child => child.State == ExchangeOrderState.Open);
    }

    // Requirement "Жизнь сделки": only a trading Worker keeps a trade, as only it keeps bots.
    [Test]
    public async Task Only_a_trading_worker_keeps_a_trade()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        _scenario.StartWorker(settings: new Dictionary<string, string?> { ["Trading:Enabled"] = "false" });

        for (var i = 0; i < 10; i++)
        {
            _scenario.Factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }

        _scenario.Exchange.Sent.ShouldBeEmpty();

        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status.ShouldBe(LiveOrderStatus.Active);
    }

    // Requirement "Ошибка сделки": a refusal for good is the end of the trade, with the reason of Bybit.
    [Test]
    public async Task Entry_refused_for_good_fails_the_trade_with_the_reason_of_bybit()
    {
        _scenario.Exchange.Refusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Insufficient balance."));
        IdeaTrade trade = await _scenario.CreateTradeAsync();

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        (await _scenario.TradeAsync(trade.Id)).Reason.ShouldBe("Insufficient balance.");
        (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status.ShouldBe(LiveOrderStatus.Rejected);
    }

    [Test]
    public async Task Entry_refused_for_a_while_is_sent_again_with_the_same_id()
    {
        _scenario.Exchange.Refusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Transient, Reason: "Too many visits."));
        IdeaTrade trade = await _scenario.CreateTradeAsync();

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status == LiveOrderStatus.Active, "the entry on the exchange");
        _scenario.Exchange.Sent.Select(order => order.ClientId).Distinct().ShouldBe([new IdeaTradeOrderId(trade.Id, 1).Code]);
        _scenario.Exchange.Sent.Count.ShouldBe(2);
    }

    // Requirement "Ошибка сделки": an order the exchange will not cancel is never left unwatched.
    [Test]
    public async Task Cancel_refused_for_good_fails_the_trade_with_the_order_named()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Not allowed."));

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestCancel());

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        (await _scenario.TradeAsync(trade.Id)).Reason!.ShouldContain($"Bybit refused to cancel order {new IdeaTradeOrderId(trade.Id, 1).Code}: Not allowed.");
    }

    // Requirement "Отмена и закрытие сделки": the entry filled before the cancel came, so the trade is open and the request is dropped.
    [Test]
    public async Task Cancel_asked_after_the_entry_filled_leaves_the_trade_open()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked with { CancelRequested = true });

        await _scenario.WaitAsync(async () => !(await _scenario.TradeAsync(trade.Id)).CancelRequested, "the request dropped");

        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Open);
        _scenario.Exchange.Cancels.ShouldBeEmpty();
    }

    // Requirement "Жизнь сделки": a pullback filled in part when its time is out stays open with what it filled.
    [Test]
    public async Task Pullback_filled_in_part_by_its_deadline_cancels_the_rest_and_stays_open()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.Fill(entry, 0.2m, 105m, fee: 0.01m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");

        _scenario.Factory.Time.Advance(trade.EntryDeadline!.Value - _scenario.Factory.Time.GetUtcNow());

        await _scenario.WaitAsync(() => _scenario.Exchange.Cancels.Count == 1, "the cancel of the rest");
        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Count == 1 && (await _scenario.TradeAsync(trade.Id)).CancelRequested == false, "the end of the limit");
        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        (open.Status, open.HeldQuantity).ShouldBe((IdeaTradeStatus.Open, 0.2m));
    }

    // Requirement "Восстановление сделки": a fill the stream missed is read when the stream is back.
    [Test]
    public async Task Fill_missed_while_the_stream_was_down_is_read_when_it_is_back()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m, silently: true);

        _scenario.Exchange.RestoreAccount();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");
        (await _scenario.TradeAsync(trade.Id)).HeldQuantity.ShouldBe(0.47m);
    }

    // Requirement "Сверка после чужого исполнения": the owner closes the position by hand and the comparison fails the trade, not the stop and the target.
    [Test]
    public async Task Foreign_fill_on_the_symbol_sets_the_comparison_off_for_a_trade()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();
        _scenario.Exchange.Holding = 0m;

        _scenario.Exchange.ForeignFill("manual-1", 0.47m, 105m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();
    }

    // Requirement "Закрыть по рынку": the stop fired meanwhile, so the close finds its stop and target done and the trade closes by the stop.
    [Test]
    public async Task Close_asked_after_the_stop_fired_silently_ends_with_the_stop()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.Exchange.TriggerChild(entry, StopOrderKind.StopLoss, 100m, silently: true);

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtStop, "the stop");
        _scenario.Exchange.Orders.Count.ShouldBe(1);
    }

    // On the spot the fee of a buy is taken from the coins bought, so the trade holds fewer.
    [Test]
    public async Task Fee_in_the_coin_bought_on_the_spot_is_taken_off_the_quantity_held()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(instrument: LiveTradingScenario.Spot);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");

        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.00047m, feeCoin: "BTC");

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");
        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        (open.HeldQuantity, open.Entry.Fee).ShouldBe((0.46953m, 0.04935m));
    }

    // Requirement "Ошибка сделки": the cancel of an entry Bybit refuses is asked again while the entry stands, though the trade is failed.
    [Test]
    public async Task Cancel_refused_for_good_is_asked_again_until_the_entry_is_gone()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Not allowed."));
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestCancel());
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");

        await SettleAsync();

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status == LiveOrderStatus.Cancelled, "the entry cancelled");
        _scenario.Exchange.Cancels.Count.ShouldBe(2);
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Failed);
    }

    // Requirement "Сверка с биржей": a trade that fails while it waits takes its entry off, which carries a stop and a target of its own.
    [Test]
    public async Task Entry_that_waits_is_cancelled_when_the_comparison_fails_the_trade()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.Holding = 1m;

        _scenario.Exchange.ForeignFill("manual-1", 1m, 105m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        await _scenario.WaitAsync(() => _scenario.Exchange.Cancels.Count == 1, "the cancel of the entry");
        _scenario.Exchange.Cancels.ShouldBe([entry]);
    }

    // A failed trade whose entry still stands is taken by a Worker again, which takes the entry off.
    [Test]
    public async Task Failed_trade_whose_entry_still_stands_is_taken_again_and_the_entry_is_cancelled()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        WorkerHost first = _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        await _scenario.StopWorkerAsync(first);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.Fail("Bybit refused to cancel.", _scenario.Factory.Time.GetUtcNow()));

        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Cancels.Count == 1, "the cancel of the entry");
        _scenario.Exchange.Cancels.ShouldBe([entry]);
        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status == LiveOrderStatus.Cancelled, "the entry cancelled");
    }

    // Requirement "Восстановление сделки": the stop fired and the target went with it while no Worker listened and no child was saved.
    [Test]
    public async Task Stop_and_target_that_are_finished_when_the_worker_looks_are_read_from_the_history_of_the_entry()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m, silently: true);
        _scenario.Exchange.TriggerChild(entry, StopOrderKind.StopLoss, 100m, silently: true);

        _scenario.Exchange.RestoreAccount();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtStop, "the stop");
        (await _scenario.TradeAsync(trade.Id)).Result.ShouldBe((0.47m * 100m) - (0.47m * 105m) - 0.02m);
    }

    // Requirement "Вход в сделку": a market entry sized at the click is not sent minutes later, when the price is another.
    [Test]
    public async Task Market_entry_not_taken_within_thirty_seconds_is_not_sent_and_the_trade_is_cancelled()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        _scenario.Factory.Time.Advance(TimeSpan.FromSeconds(31));

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Cancelled, "the cancelled trade");
        (await _scenario.TradeAsync(trade.Id)).Reason.ShouldBe("Вход не ушёл вовремя: Bybit не принял его за 30 секунд.");
        (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status.ShouldBe(LiveOrderStatus.Cancelled);
        _scenario.Exchange.Sent.ShouldBeEmpty();
    }

    // The answer of Bybit may be lost while it took the order: a late send finds it and does not give the entry up.
    [Test]
    public async Task Market_entry_that_bybit_took_though_the_answer_was_lost_is_not_given_up()
    {
        _scenario.Exchange.Refusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Transient, Reason: "Timeout."));
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Sent.Count == 1, "the lost send");
        _scenario.Exchange.Place(entry, OrderSide.Buy, 0.47m);

        _scenario.Factory.Time.Advance(TimeSpan.FromSeconds(31));

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status == LiveOrderStatus.Active, "the entry found");
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.AwaitingEntry);
        _scenario.Exchange.Sent.Count.ShouldBe(1);
    }

    /// <summary>A bot long on the linear market with the position 1; a Worker runs.</summary>
    private async Task<Guid> BotWithPositionAsync()
    {
        Guid bot = await _scenario.CreateBotAsync(category: "linear");
        _scenario.StartWorker();
        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the start order");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);
        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the open position of the bot");
        await SettleAsync();

        return bot;
    }

    /// <summary>A trade whose entry is filled and whose stop and target stand; a Worker runs.</summary>
    private async Task<IdeaTrade> OpenTradeAsync(decimal quantity)
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(quantity: quantity);

        if (_scenario.Workers.Count == 0)
        {
            _scenario.StartWorker();
        }

        int sent = _scenario.Exchange.Orders.Count;
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == sent + 1, "the entry");
        _scenario.Exchange.Fill(new IdeaTradeOrderId(trade.Id, 1).Code, quantity, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");

        return trade;
    }

    private async Task SettleAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            _scenario.Factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }
    }
}