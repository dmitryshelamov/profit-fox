using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.MarketData;
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
        await _scenario.WaitAsync(async () => (await _scenario.TradeFillsAsync(trade.Id)).Count == 2, "the fills of the entry and the target");
        (await _scenario.TradeFillsAsync(trade.Id)).Select(fill => (fill.Quantity, fill.FeeAsset)).ShouldBe([(0.47m, FeeAsset.Quote), (0.47m, FeeAsset.Quote)]);
    }

    // Scenario "Символ с плечом 10×": the entry made through the API after it set the leverage goes to the exchange as before.
    [Test]
    public async Task Entry_on_linear_after_the_leverage_is_set_is_sent_as_before()
    {
        _scenario.Factory.Ledger.GetLinearPositionAsync(Arg.Any<ExchangeKey>(), LiveTradingScenario.Linear, Arg.Any<CancellationToken>()).Returns(new LinearPosition(10m, 0m));
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        HttpResponseMessage created = await _scenario.Client.PostAsJsonAsync("/api/idea-trades", new { ideaId = idea, accountId = _scenario.Account, amount = 47.3m });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        IdeaTrade trade = await _scenario.TradeAsync((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await _scenario.Factory.Orders.Received(1).SetLeverageAsync(Arg.Any<ExchangeKey>(), LiveTradingScenario.Linear, 1, Arg.Any<CancellationToken>());

        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Orders.Single().ShouldBe(new OrderRequest(new IdeaTradeOrderId(trade.Id, 1).Code, OrderSide.Buy, OrderType.Market, null, trade.Quantity, null, new Protection(104m, 98m)));
        _scenario.Exchange.LeverageSets.ShouldBeEmpty();
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
        await _scenario.WaitAsync(async () => (await _scenario.TradeFillsAsync(trade.Id)).Count == 2, "the fills of the entry and the close");
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
        closed.Reason.ShouldBeNull();
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

        // The executions of the entry, the close and the two children are read once, though the session came round again for the cancel.
        _scenario.Exchange.OrderFillReads.ShouldBe(4);
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
        await SettleAsync();
        _scenario.Exchange.ChildCancels.Count.ShouldBe(2);
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

    // Scenarios "Вход сделки по идее рядом с ботом" and "Стоп сработал рядом с ботом": the entry and the stop belong to the trade, so
    // the neighbour bot takes neither for a foreign fill.
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

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status == LiveOrderStatus.Active, "the entry taken");
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
        await _scenario.WaitAsync(async () => (await _scenario.TradeFillsAsync(trade.Id)).Count == 1, "the fill of the entry");
        (await _scenario.TradeFillsAsync(trade.Id)).Single().FeeAsset.ShouldBe(FeeAsset.Base);
    }

    // Requirement "Отмена и закрытие сделки": a second click on «Закрыть по рынку» does not sell the position twice.
    [Test]
    public async Task Second_close_asked_while_the_first_stands_places_nothing()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked with { CloseRequested = true });
        await SettleAsync();

        _scenario.Exchange.Orders.Count.ShouldBe(2);
    }

    // The close of an old trade goes: the time to send runs for a market entry only.
    [Test]
    public async Task Close_of_an_old_trade_is_sent()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        _scenario.Factory.Time.Advance(TimeSpan.FromMinutes(5));

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
    }

    [Test]
    public async Task Pullback_limit_older_than_the_time_to_send_a_market_entry_is_sent()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.Factory.Time.Advance(TimeSpan.FromSeconds(31));

        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.AwaitingEntry);
    }

    // The owner closes a pullback that is filled in part: the rest of the limit goes first, then what it filled is sold.
    [Test]
    public async Task Close_asked_while_the_limit_stands_takes_the_limit_off_and_then_sells_what_is_held()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.Fill(entry, 0.2m, 105m, fee: 0.01m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(() => _scenario.Exchange.Cancels.Count == 1, "the cancel of the rest of the limit");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.Orders.Last().Quantity.ShouldBe(0.2m);
    }

    // The trade is cancelled before its entry went to the exchange: the cancel finds nothing there and ends the order.
    [Test]
    public async Task Cancel_asked_before_the_entry_was_sent_ends_it_without_sending()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestCancel());

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Cancelled, "the cancelled trade");
        (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status.ShouldBe(LiveOrderStatus.Cancelled);
        _scenario.Exchange.Sent.ShouldBeEmpty();
    }

    // The order the exchange finished without a new fill ends in the books too.
    [Test]
    public async Task Entry_finished_by_the_exchange_without_a_new_fill_is_filled_in_the_books()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.Fill(entry, 0.2m, 105m, fee: 0.01m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");

        _scenario.Exchange.Finish(entry);

        await _scenario.WaitAsync(async () => (await _scenario.TradeOrdersAsync(trade.Id)).Single().Status == LiveOrderStatus.Filled, "the entry filled");
    }

    // Another trade on the same symbol: its stop is not the stop of this one.
    [Test]
    public async Task Stop_of_another_trade_does_not_touch_this_one()
    {
        IdeaTrade first = await OpenTradeAsync(0.47m);
        IdeaTrade second = await OpenTradeAsync(0.3m);

        await SettleAsync();
        int reads = _scenario.Exchange.HoldingReads;

        _scenario.Exchange.TriggerChild(new IdeaTradeOrderId(second.Id, 1).Code, StopOrderKind.StopLoss, 100m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(second.Id)).Status == IdeaTradeStatus.ClosedAtStop, "the stop of the second");
        await SettleAsync();
        _scenario.Exchange.HoldingReads.ShouldBe(reads);
        IdeaTrade untouched = await _scenario.TradeAsync(first.Id);
        (untouched.Status, untouched.HeldQuantity, untouched.StopLoss).ShouldBe((IdeaTradeStatus.Open, 0.47m, LegTotals.None));
    }

    // The stop or the target may fire with an update that no longer names its entry: the books know it by its exchange id.
    [Test]
    public async Task Target_told_without_its_parent_still_closes_the_trade()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        _scenario.Exchange.BareChildUpdates = true;

        _scenario.Exchange.TriggerChild(new IdeaTradeOrderId(trade.Id, 1).Code, StopOrderKind.TakeProfit, 115m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtTarget, "the target");
    }

    // A trade that waits does not look for a stop and a target that cannot be there yet.
    [Test]
    public async Task Trade_that_waits_does_not_look_for_its_stop_and_target()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.RestoreAccount();
        await SettleAsync();

        _scenario.Exchange.ParentReads.ShouldBe(0);
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.AwaitingEntry);
    }

    // An execution of the trade's own order that names no order of the exchange cannot be kept with it, and does no harm.
    [Test]
    public async Task Execution_that_names_no_order_of_the_exchange_is_not_kept()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();
        int before = (await _scenario.TradeFillsAsync(trade.Id)).Count;

        _scenario.Exchange.ForeignFill(new IdeaTradeOrderId(trade.Id, 1).Code, 0.1m, 105m, LiveTradingScenario.Linear);
        await SettleAsync();

        (await _scenario.TradeFillsAsync(trade.Id)).Count.ShouldBe(before);
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Open);
    }

    // The stop and the target of a trade that is told to be gone are no one's to refuse twice, and the others go on.
    [Test]
    public async Task Stop_that_cannot_be_taken_off_for_good_is_not_asked_again_while_the_other_is_retried()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Not allowed."));
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Transient, Reason: "Too many visits."));

        _scenario.Exchange.Fill(new IdeaTradeOrderId(trade.Id, 2).Code, 0.47m, 110m, fee: 0.03m);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedManually, "the closed trade");
        await SettleAsync();
        await SettleAsync();
        _scenario.Exchange.ChildCancels.Count.ShouldBe(3);
        (await _scenario.TradeChildrenAsync(trade.Id)).Count(child => child.Finished).ShouldBe(1);
    }

    // A trade that is failed compares nothing: a foreign fill does not set the comparison off for its neighbours.
    [Test]
    public async Task Failed_trade_does_not_compare_the_exchange_on_a_foreign_fill()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        for (var i = 0; i < 20; i++)
        {
            _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Not allowed."));
        }

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestCancel());
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        await SettleAsync();
        int reads = _scenario.Exchange.HoldingReads;

        _scenario.Exchange.ForeignFill("manual-1", 0.1m, 105m, LiveTradingScenario.Linear);
        await SettleAsync();

        _scenario.Exchange.HoldingReads.ShouldBe(reads);
    }

    // A fill on another instrument is no reason to compare this one.
    [Test]
    public async Task Foreign_fill_on_another_instrument_does_not_set_the_comparison_off()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();
        _scenario.Exchange.Holding = 0m;

        _scenario.Exchange.ForeignFill("manual-1", 0.47m, 105m, LiveTradingScenario.Spot);
        await SettleAsync();

        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Open);
    }

    // One comparison serves the foreign fills that come together.
    [Test]
    public async Task Foreign_fills_that_come_together_are_compared_once()
    {
        await OpenTradeAsync(0.47m);
        await SettleAsync();
        int before = _scenario.Exchange.HoldingReads;
        _scenario.Exchange.ForeignFill("manual-1", 0.1m, 105m, LiveTradingScenario.Linear);
        await SettleAsync();
        int one = _scenario.Exchange.HoldingReads - before;

        before = _scenario.Exchange.HoldingReads;
        _scenario.Exchange.ForeignFill("manual-2", 0.1m, 105m, LiveTradingScenario.Linear);
        _scenario.Exchange.ForeignFill("manual-3", 0.1m, 105m, LiveTradingScenario.Linear);
        _scenario.Exchange.ForeignFill("manual-4", 0.1m, 105m, LiveTradingScenario.Linear);
        await SettleAsync();

        (_scenario.Exchange.HoldingReads - before).ShouldBe(one);
    }

    // Requirement "Сверка после чужого исполнения": the owner closes the position by hand and the comparison fails the trade, not the
    // stop and the target. A difference is read again right away and once more after a pause, which a neighbour needs to write its own fill.
    [Test]
    public async Task Difference_is_read_again_after_a_pause_before_the_trade_fails()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();
        _scenario.Exchange.Holding = 0m;

        _scenario.Exchange.ForeignFill("manual-1", 0.47m, 105m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        _scenario.Exchange.ChildCancels.ShouldBeEmpty();
        IReadOnlyList<DateTimeOffset> reads = _scenario.Exchange.HoldingReadTimes;
        (reads[^1] - reads[^2]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3));
    }

    // Requirement "Ошибка сделки": an order the exchange will not cancel fails the trade with the order named and is never left
    // unwatched: its cancel is asked again while the entry stands, though the trade is failed.
    [Test]
    public async Task Cancel_refused_for_good_is_asked_again_until_the_entry_is_gone()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Not allowed."));
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestCancel());
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Failed, "the failed trade");
        (await _scenario.TradeAsync(trade.Id)).Reason!.ShouldContain($"Bybit refused to cancel order {new IdeaTradeOrderId(trade.Id, 1).Code}: Not allowed.");

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

    // Requirement "Восстановление сделки": the entry filled, the stop fired and the target went with it while no Worker listened and no
    // child was saved; all of it is read when the stream is back.
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

    // Scenarios "Ликвидация закрыла сделку" and "Ликвидация с результатом": the liquidation of Bybit is an order of its own with no client id of ours;
    // it closes the trade with the result of its execution, and no comparison fails the trade for the position it took.
    [Test]
    public async Task Liquidation_on_the_stream_closes_the_trade_with_the_result_of_its_execution()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();

        _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.47m, 95m, 0.03m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Liquidated, "the liquidated trade");
        IdeaTrade liquidated = await _scenario.TradeAsync(trade.Id);
        liquidated.Liquidation.ShouldBe(new LegTotals(0.47m, 0.47m * 95m, 0.03m));
        liquidated.Result.ShouldBe((0.47m * 95m) - (0.47m * 105m) - 0.02m - 0.03m);
        liquidated.ResultR.ShouldBe(liquidated.Result / 2m);
        await SettleAsync();
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Liquidated);
    }

    // Decision 4: Bybit usually cancels the stop and the target of a position it liquidated; those it left are taken off, as after a market close,
    // and the executions of the orders of the trade are kept with it.
    [TestCase(ExchangeFillKind.Liquidation, IdeaTradeStatus.Liquidated)]
    [TestCase(ExchangeFillKind.Adl, IdeaTradeStatus.ClosedByAdl)]
    public async Task Trade_closed_by_the_exchange_takes_off_the_stop_and_the_target_left_and_keeps_its_fills(ExchangeFillKind kind, IdeaTradeStatus status)
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m, trades: false);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");

        _scenario.Exchange.ForceFill(kind, OrderSide.Sell, 0.47m, 95m, 0.03m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(() => _scenario.Exchange.ChildCancels.Count == 2, "the stop and the target taken off");
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).All(child => child.Finished), "the stop and the target ended");
        await _scenario.WaitAsync(async () => (await _scenario.TradeFillsAsync(trade.Id)).Count == 1, "the fill of the entry");
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(status);
    }

    // Scenario "ADL закрыла сделку": a short bought back by the auto-deleveraging of Bybit.
    [Test]
    public async Task Auto_deleveraging_on_the_stream_closes_a_short_by_the_exchange()
    {
        IdeaTrade trade = await OpenShortAsync(0.4m);

        _scenario.Exchange.ForceFill(ExchangeFillKind.Adl, OrderSide.Buy, 0.4m, 100m, 0.02m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedByAdl, "the trade closed by ADL");
        IdeaTrade closed = await _scenario.TradeAsync(trade.Id);
        closed.Adl.ShouldBe(new LegTotals(0.4m, 40m, 0.02m));
        closed.Result.ShouldBe((0.4m * 105m) - 40m - 0.02m - 0.02m);
    }

    // Scenario "Две сделки на символе": 0.3 and 0.1 held, 0.4 liquidated with the fee 0.4; each session applies its own share.
    [Test]
    public async Task Liquidation_of_two_trades_of_the_side_gives_each_its_share()
    {
        IdeaTrade first = await OpenTradeAsync(0.3m);
        IdeaTrade second = await OpenTradeAsync(0.1m);
        await SettleAsync();

        _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.4m, 95m, 0.4m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(
            async () => (await _scenario.TradeAsync(first.Id)).Status == IdeaTradeStatus.Liquidated && (await _scenario.TradeAsync(second.Id)).Status == IdeaTradeStatus.Liquidated,
            "both trades liquidated");
        (await _scenario.TradeAsync(first.Id)).Liquidation.ShouldBe(new LegTotals(0.3m, 28.5m, 0.3m));
        (await _scenario.TradeAsync(second.Id)).Liquidation.ShouldBe(new LegTotals(0.1m, 9.5m, 0.1m));
    }

    // Scenario "Исполнение пришло дважды": a liquidation of a part told twice takes the part out once.
    [Test]
    public async Task Liquidation_told_twice_takes_its_part_out_once()
    {
        IdeaTrade trade = await OpenTradeAsync(1m);
        await SettleAsync();
        ExchangeFill fill = _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.4m, 95m, 0.04m, LiveTradingScenario.Linear);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Liquidation.Quantity == 0.4m, "the part liquidated");

        _scenario.Exchange.RepeatFill(fill);
        await SettleAsync();

        IdeaTrade open = await _scenario.TradeAsync(trade.Id);
        (open.Status, open.HeldQuantity, open.Liquidation).ShouldBe((IdeaTradeStatus.Open, 0.6m, new LegTotals(0.4m, 38m, 0.04m)));
    }

    // Scenario "Ликвидация, пока Worker лежал": the liquidation is read from the history of the symbol since the trade opened, not failed.
    [Test]
    public async Task Liquidation_while_the_worker_was_down_closes_the_trade_from_the_history()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.StopWorkerAsync(_scenario.Workers.Single());
        _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.47m, 95m, 0.03m, LiveTradingScenario.Linear, silently: true);

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status != IdeaTradeStatus.Open, "the trade closed");
        IdeaTrade liquidated = await _scenario.TradeAsync(trade.Id);
        (liquidated.Status, liquidated.Reason).ShouldBe((IdeaTradeStatus.Liquidated, null));
        liquidated.Result.ShouldBe((0.47m * 95m) - (0.47m * 105m) - 0.02m - 0.03m);
    }

    // Scenario "Ликвидация не уронила сделку": the stream missed the liquidation, and the comparison a foreign fill set off finds it in the history
    // when it reads the exchange again, before it fails anything: 0 on the exchange against 0 in the books.
    [Test]
    public async Task Liquidation_the_stream_missed_is_found_before_the_comparison_fails_the_trade()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();
        _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.47m, 95m, 0.03m, LiveTradingScenario.Linear, silently: true);

        _scenario.Exchange.ForeignFill("manual-1", 0.1m, 95m, LiveTradingScenario.Linear);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status != IdeaTradeStatus.Open, "the trade closed");
        await SettleAsync();
        (await _scenario.TradeAsync(trade.Id)).Status.ShouldBe(IdeaTradeStatus.Liquidated);
        _scenario.Exchange.Holding.ShouldBe(0m);
    }

    // The history of the liquidations is asked only for an open trade on linear that knows when it opened: the spot, a trade that waits and a
    // trade an older Worker opened ask nothing.
    [Test]
    public async Task Trade_on_the_spot_does_not_ask_for_liquidations()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(instrument: LiveTradingScenario.Spot);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(new IdeaTradeOrderId(trade.Id, 1).Code, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.Open, "the open trade");

        _scenario.Exchange.RestoreAccount();
        await SettleAsync();

        _scenario.Exchange.ForcedFillReads.ShouldBe(0);
    }

    [Test]
    public async Task Trade_that_waits_does_not_ask_for_liquidations()
    {
        await _scenario.CreateTradeAsync(IdeaType.Pullback);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");

        _scenario.Exchange.RestoreAccount();
        await SettleAsync();

        _scenario.Exchange.ForcedFillReads.ShouldBe(0);
    }

    [Test]
    public async Task Trade_without_an_open_time_does_not_ask_for_liquidations()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await _scenario.StopWorkerAsync(_scenario.Workers.Single());
        await _scenario.ChangeTradeAsync(trade.Id, locked => locked with { OpenedAt = null });
        int reads = _scenario.Exchange.ForcedFillReads;

        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.AccountSubscribers == 1, "the trade taken again");
        await SettleAsync();

        _scenario.Exchange.ForcedFillReads.ShouldBe(reads);
    }

    // A liquidation of another symbol of the account is no exit of this trade.
    [Test]
    public async Task Liquidation_of_another_symbol_does_not_touch_the_trade()
    {
        IdeaTrade trade = await OpenTradeAsync(0.47m);
        await SettleAsync();

        _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.47m, 95m, 0.03m, new Instrument(MarketCategory.Linear, "ETHUSDT"));
        await SettleAsync();

        IdeaTrade untouched = await _scenario.TradeAsync(trade.Id);
        (untouched.Status, untouched.HeldQuantity, untouched.Liquidation).ShouldBe((IdeaTradeStatus.Open, 0.47m, LegTotals.None));
    }

    // Scenario "Сделка другой стороны": a liquidation that sells closes no short, and it sets no comparison off.
    [Test]
    public async Task Liquidation_that_sells_does_not_touch_a_short()
    {
        IdeaTrade trade = await OpenShortAsync(0.47m);
        await SettleAsync();
        int reads = _scenario.Exchange.HoldingReads;

        _scenario.Exchange.ForceFill(ExchangeFillKind.Liquidation, OrderSide.Sell, 0.47m, 95m, 0.03m, LiveTradingScenario.Linear);
        await SettleAsync();

        IdeaTrade untouched = await _scenario.TradeAsync(trade.Id);
        (untouched.Status, untouched.HeldQuantity, untouched.Liquidation).ShouldBe((IdeaTradeStatus.Open, 0.47m, LegTotals.None));
        _scenario.Exchange.HoldingReads.ShouldBe(reads);
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

    /// <summary>A short whose entry at 105 is filled and whose stop and target stand; a Worker runs.</summary>
    private async Task<IdeaTrade> OpenShortAsync(decimal quantity)
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(side: IdeaSide.Short, quantity: quantity);

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