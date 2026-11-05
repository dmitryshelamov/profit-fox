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

    // Scenario "Закрыть по рынку": the stop and the target come off, then the position is closed by the market that only reduces it.
    [Test]
    public async Task Close_by_the_market_takes_the_stop_and_the_target_off_and_sells_what_is_held()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");

        await _scenario.ChangeTradeAsync(trade.Id, locked => locked.RequestClose());

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.ChildCancels.Count.ShouldBe(2);
        string close = new IdeaTradeOrderId(trade.Id, 2).Code;
        _scenario.Exchange.Orders.Last().ShouldBe(new OrderRequest(close, OrderSide.Sell, OrderType.Market, null, 0.47m, null, ReduceOnly: true));
        _scenario.Exchange.Fill(close, 0.47m, 110m, fee: 0.03m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedManually, "the closed trade");
        (await _scenario.TradeAsync(trade.Id)).Result.ShouldBe((0.47m * 110m) - (0.47m * 105m) - 0.02m - 0.03m);
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