using System.Collections.Concurrent;
using System.Text.Json;

using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;

using ProfitFox.Tests.Integration.Infrastructure;

using Serilog.Core;
using Serilog.Events;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>A trading Worker keeps bots on the exchange: orders without losses or repeats, positions from fills, restarts from the database.</summary>
public class LiveTradingTests
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

    // Scenarios "Запуск остановленного бота" and "Ордер сохраняется до отправки": DCA buys 100 at the start, at the last close 100.
    [Test]
    public async Task Started_bot_sends_its_start_order_and_its_fill_opens_the_position()
    {
        Guid bot = await _scenario.CreateBotAsync();
        _scenario.StartWorker();

        await _scenario.ActAsync(bot, "start");

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the start order");
        _scenario.Exchange.Orders.Single().ShouldBe(new OrderRequest(LiveTradingScenario.ClientId(bot, 1), OrderSide.Buy, OrderType.Market, null, 1m, null));
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m, fee: 0.001m, feeCoin: "BTC");
        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Open == true, "the open position");
        LivePosition position = (await _scenario.PositionsAsync(bot))[0];
        (position.Number, position.Quantity, position.Invested).ShouldBe((1, 0.999m, 100m));
        (await _scenario.OrdersAsync(bot)).Single().Status.ShouldBe(LiveOrderStatus.Filled);
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
    }

    // Scenarios "Рестарт бота DCA" and "Рестарт посреди позиции": the start event and the start order come once.
    [Test]
    public async Task Restarted_worker_goes_on_without_a_second_start_order()
    {
        Guid bot = await StartedAndFilledAsync();
        WorkerHost first = WorkerOf();
        await _scenario.StopWorkerAsync(first);

        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Subscribers == 1, "the bot taken again");
        await SettleAsync();

        _scenario.Exchange.Sent.Count.ShouldBe(1);
        (await _scenario.PositionsAsync(bot))[0].Quantity.ShouldBe(1m);
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
    }

    // Scenario "Повтор исполнения".
    [Test]
    public async Task Repeated_fill_counts_once()
    {
        Guid bot = await StartedAndFilledAsync();

        _scenario.Exchange.Repeat(LiveTradingScenario.ClientId(bot, 1));
        await SettleAsync();

        (await _scenario.PositionsAsync(bot))[0].Quantity.ShouldBe(1m);
    }

    // Scenario "Чужой ордер".
    [Test]
    public async Task Fill_of_an_order_of_nobody_in_the_system_is_ignored()
    {
        Guid bot = await StartedAndFilledAsync();

        _scenario.Exchange.ForeignFill("manual-1", 5m, 100m);
        await SettleAsync();

        (await _scenario.PositionsAsync(bot))[0].Quantity.ShouldBe(1m);
    }

    // Scenario "Исполнение во время обрыва".
    [Test]
    public async Task Fill_missed_while_the_stream_was_down_is_read_again()
    {
        Guid bot = await StartedAsync();

        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m, silently: true);
        _scenario.Exchange.Restore();

        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the missed fill");
    }

    // Bybit tells the end of an order on its own topic, often before the fill, and its trade history lags behind: the order
    // waits for the fills the exchange counted.
    [Test]
    public async Task Fill_that_comes_after_the_end_of_its_order_counts()
    {
        Guid bot = await StartedAsync();
        string start = LiveTradingScenario.ClientId(bot, 1);

        _scenario.Exchange.FillLate(start, 1m, 100m);
        await SettleAsync();
        _scenario.Exchange.Deliver(start);

        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the late fill");
        (await _scenario.OrdersAsync(bot)).Single().Status.ShouldBe(LiveOrderStatus.Filled);
    }

    // The late fill never comes on the stream: the trade history is read again until it has it.
    [Test]
    public async Task Fill_missing_from_the_trade_history_is_read_again_later()
    {
        Guid bot = await StartedAsync();
        string start = LiveTradingScenario.ClientId(bot, 1);

        _scenario.Exchange.FillLate(start, 1m, 100m);
        await SettleAsync();
        _scenario.Exchange.Deliver(start, silently: true);

        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the fill read again");
        (await _scenario.OrdersAsync(bot)).Single().Status.ShouldBe(LiveOrderStatus.Filled);
    }

    // A fill read after a restart reaches the strategy once its indicators are warm: the sell limit goes at the average of
    // the history, not at an average of nothing.
    [Test]
    public async Task Fill_read_after_a_restart_meets_warm_indicators()
    {
        Guid bot = await _scenario.CreateBotAsync("ema-limit", JsonDocument.Parse(EmaLimit).RootElement);
        WorkerHost first = _scenario.StartWorker();
        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).SingleOrDefault()?.Status == LiveOrderStatus.Active, "the start order on the exchange");
        await _scenario.StopWorkerAsync(first);
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m, silently: true);

        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the sell limit");
        OrderRequest sell = _scenario.Exchange.Orders.Single(order => order.ClientId == LiveTradingScenario.ClientId(bot, 2));
        (sell.Side, sell.Type, sell.Price, sell.Quantity).ShouldBe((OrderSide.Sell, OrderType.Limit, 110m, 1m));
    }

    // Scenarios "Worker остановлен до отправки" and "Ответ на отправку потерян": the saved order goes out after the restart, once.
    [Test]
    public async Task Order_saved_before_the_worker_stopped_goes_out_once_after_it()
    {
        Guid bot = await _scenario.CreateBotAsync();
        _scenario.Exchange.Hold = new TaskCompletionSource();
        WorkerHost first = _scenario.StartWorker();
        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).Count == 1, "the saved order");
        await _scenario.StopWorkerAsync(first);
        _scenario.Exchange.Hold = null;

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).Single().Status == LiveOrderStatus.Active, "the order on the exchange");
        _scenario.Exchange.Orders.Count.ShouldBe(1);
    }

    // Scenario "Лимит запросов".
    [Test]
    public async Task Transient_refusal_is_sent_again_later()
    {
        Guid bot = await _scenario.CreateBotAsync();
        _scenario.Exchange.Refusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Transient, Reason: "Too many visits."));
        _scenario.StartWorker();

        await _scenario.ActAsync(bot, "start");

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the order sent again");
        _scenario.Exchange.Sent.Count.ShouldBe(2);
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
    }

    // Scenario "Нет денег".
    [Test]
    public async Task Permanent_refusal_stops_the_bot_with_the_reason()
    {
        Guid bot = await _scenario.CreateBotAsync();
        _scenario.Exchange.Refusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "Insufficient balance."));
        _scenario.StartWorker();

        await _scenario.ActAsync(bot, "start");

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Failed, "the failed bot");
        (await _scenario.LiveAsync(bot)).StopReason.ShouldBe("Insufficient balance.");
        (await _scenario.OrdersAsync(bot)).Single().Status.ShouldBe(LiveOrderStatus.Rejected);
    }

    // Scenario "Исполнение на паузе": the fill counts, the strategy places nothing on it.
    [Test]
    public async Task Paused_bot_keeps_its_books_and_places_nothing()
    {
        Guid bot = await StartedAsync("ladder");
        await _scenario.ActAsync(bot, "pause");
        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Paused, "the pause");
        await SettleAsync();

        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);

        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the fill on pause");
        await SettleAsync();
        _scenario.Exchange.Orders.Count.ShouldBe(1);
    }

    // Scenarios "Остановка с лимитками" and "Закрытие лонга".
    [Test]
    public async Task Stopped_bot_cancels_its_orders_keeps_its_position_and_closes_it_on_request()
    {
        Guid bot = await StartedAsync("ladder");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 3, "the limit orders of the ladder");

        await _scenario.ActAsync(bot, "stop");

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Stopped, "the stopped bot");
        _scenario.Exchange.Cancels.ShouldBe([LiveTradingScenario.ClientId(bot, 2), LiveTradingScenario.ClientId(bot, 3)], ignoreOrder: true);
        (await _scenario.PositionsAsync(bot))[0].Quantity.ShouldBe(1m);

        await _scenario.ActAsync(bot, "close-position");

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 4, "the market exit");
        OrderRequest exit = _scenario.Exchange.Orders.Single(order => order.ClientId == LiveTradingScenario.ClientId(bot, 4));
        (exit.Side, exit.Type, exit.Quantity).ShouldBe((OrderSide.Sell, OrderType.Market, 1m));
        _scenario.Exchange.Fill(exit.ClientId, 1m, 101m);
        await _scenario.WaitAsync(async () => !(await _scenario.LiveAsync(bot)).CloseRequested, "the closed position");
        (await _scenario.PositionsAsync(bot)).Single(position => position.Number == 1).ClosedAt.ShouldNotBeNull();
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Stopped);
    }

    // Scenario "Позицию закрыли вручную".
    [Test]
    public async Task Position_the_exchange_does_not_hold_stops_the_bot()
    {
        Guid bot = await StartedAndFilledAsync();
        await _scenario.StopWorkerAsync(WorkerOf());
        _scenario.Exchange.Holding = 0m;

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Failed, "the failed bot");
        (await _scenario.LiveAsync(bot)).StopReason!.ShouldContain("less than the positions of the bots 1");
    }

    // Scenario "Неизвестный ордер бота".
    [Test]
    public async Task Order_of_the_bot_it_does_not_know_stops_the_bot()
    {
        Guid bot = await StartedAndFilledAsync();
        await _scenario.StopWorkerAsync(WorkerOf());
        _scenario.Exchange.Place(LiveTradingScenario.ClientId(bot, 99), OrderSide.Sell, 1m);

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Failed, "the failed bot");
        (await _scenario.LiveAsync(bot)).StopReason!.ShouldContain(LiveTradingScenario.ClientId(bot, 99));
    }

    // Scenarios "Два бота на одном символе" and "Два торгующих Worker'а".
    [Test]
    public async Task Bots_of_one_account_and_symbol_keep_their_own_positions_on_two_workers()
    {
        Guid first = await _scenario.CreateBotAsync();
        Guid second = await _scenario.CreateBotAsync();
        _scenario.StartWorker();
        _scenario.StartWorker();

        await _scenario.ActAsync(first, "start");
        await _scenario.ActAsync(second, "start");

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "a start order of each bot");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(first, 1), 1m, 100m);
        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(first)).FirstOrDefault()?.Quantity == 1m, "the fill of the first bot");
        await SettleAsync();
        _scenario.Exchange.Sent.Count.ShouldBe(2);
        (await _scenario.PositionsAsync(second)).Single().Quantity.ShouldBe(0m);
    }

    // Scenario "Свечи за долгий обрыв": of the candles missed while no Worker kept the bot, the strategy gets the last one.
    [Test]
    public async Task Last_candle_missed_by_a_stopped_worker_reaches_the_strategy()
    {
        Guid bot = await StartedAndFilledAsync();
        await _scenario.StopWorkerAsync(WorkerOf());

        for (DateTimeOffset open = LiveTradingScenario.LastOpen.AddHours(1); open <= LiveTradingScenario.LastOpen.AddHours(5); open = open.AddHours(1))
        {
            _scenario.Factory.Bybit.Klines.Add([open.ToString("O"), "100", "100", "100", "100", "1", "100"]);
        }

        _scenario.Factory.Time.Advance(TimeSpan.FromHours(5));
        _scenario.StartWorker();

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the purchase of DCA on the last missed candle");
        (await _scenario.LiveAsync(bot)).LastBarAt.ShouldBe(LiveTradingScenario.LastOpen.AddHours(5));
    }

    // The session learns of a pause with its next renewal; a candle before it meets the bot as written and is not traded.
    [Test]
    public async Task Candle_right_after_a_pause_places_nothing()
    {
        Guid bot = await StartedAndFilledAsync();
        await SettleAsync();

        await _scenario.ActAsync(bot, "pause");
        _scenario.Exchange.CloseCandle(DateTimeOffset.Parse("2024-03-10T13:00:00Z"), 100m);

        await SettleAsync();
        _scenario.Exchange.Orders.Count.ShouldBe(1);
    }

    // Scenario "Снятие с паузы": candles of the pause are only history, the strategy goes on with the next one.
    [Test]
    public async Task Resumed_bot_goes_on_with_the_next_candle()
    {
        Guid bot = await StartedAndFilledAsync();
        await _scenario.ActAsync(bot, "pause");
        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Paused, "the pause");
        await SettleAsync();
        _scenario.Exchange.CloseCandle(DateTimeOffset.Parse("2024-03-10T13:00:00Z"), 100m);
        await SettleAsync();
        _scenario.Exchange.Orders.Count.ShouldBe(1);

        await _scenario.ActAsync(bot, "resume");
        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Running, "the resume");
        await SettleAsync();
        _scenario.Exchange.CloseCandle(DateTimeOffset.Parse("2024-03-10T14:00:00Z"), 100m);

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the next purchase of DCA");
    }

    // Scenario "Аренда перехвачена": the take that lost the bot writes nothing more of it.
    [Test]
    public async Task Worker_whose_lease_was_taken_over_writes_nothing_more()
    {
        Guid bot = await StartedAsync();
        await using (var connection = new Npgsql.NpgsqlConnection(_scenario.Factory.ConnectionString))
        {
            await connection.OpenAsync();
            await Dapper.SqlMapper.ExecuteAsync(
                connection,
                "UPDATE live_bots SET lease_owner = @Owner, lease_epoch = lease_epoch + 1, leased_until = @Until WHERE bot_id = @Bot",
                new { Owner = Guid.NewGuid(), Until = DateTimeOffset.Parse("2030-01-01T00:00:00Z"), Bot = bot });
        }

        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);

        await _scenario.WaitAsync(() => _scenario.Exchange.Subscribers == 0, "the session to end");
        (await _scenario.PositionsAsync(bot)).Single().Quantity.ShouldBe(0m);
    }

    // The log tells what the Worker took and sent, and never carries the secret of the account.
    [Test]
    public async Task Log_tells_the_take_and_the_order_without_the_secret()
    {
        var events = new ConcurrentQueue<LogEvent>();
        ILogEventSink sink = Substitute.For<ILogEventSink>();
        sink.When(target => target.Emit(Arg.Any<LogEvent>())).Do(call => events.Enqueue(call.Arg<LogEvent>()));
        Guid bot = await _scenario.CreateBotAsync();
        _scenario.StartWorker(sink);

        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).SingleOrDefault()?.Status == LiveOrderStatus.Active, "the order on the exchange");

        string[] messages = [.. events.Select(logEvent => logEvent.RenderMessage() + logEvent.Exception)];
        messages.ShouldContain(message => message.Contains("Took live bot") && message.Contains(bot.ToString()));
        messages.ShouldContain(message => message.Contains(LiveTradingScenario.ClientId(bot, 1)));
        messages.ShouldNotContain(message => message.Contains("api-secret"));
    }

    // Scenario "Постоянный отказ биржи" on the stream: an order the exchange took and then refused stops the bot.
    [Test]
    public async Task Order_refused_on_the_stream_stops_the_bot()
    {
        Guid bot = await StartedAsync();

        _scenario.Exchange.Reject(LiveTradingScenario.ClientId(bot, 1), "Insufficient balance.");

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Failed, "the failed bot");
        (await _scenario.LiveAsync(bot)).StopReason.ShouldBe("Insufficient balance.");
    }

    // A new listing has no history: the start purchase goes by its amount, and the order ends with its status.
    [Test]
    public async Task Bot_without_history_buys_by_amount_and_the_status_ends_the_order()
    {
        _scenario.Factory.Bybit.Klines.Clear();
        Guid bot = await StartedAsync();
        OrderRequest start = _scenario.Exchange.Orders.Single();
        (start.Quantity, start.QuoteAmount).ShouldBe((null, 100m));

        _scenario.Exchange.Fill(start.ClientId, 0.5m, 100m, silently: true);
        _scenario.Exchange.Finish(start.ClientId);

        // The exchange says the order filled: it is filled in the books too, though only its status told the end.
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).Single().Status == LiveOrderStatus.Filled, "the filled order");
        (await _scenario.PositionsAsync(bot))[0].Quantity.ShouldBe(0.5m);
    }

    // A fill that comes during the cancel stays in the position: the exchange has nothing to cancel, its fills are read.
    [Test]
    public async Task Order_filled_before_its_cancel_is_counted_on_stop()
    {
        Guid bot = await StartedAsync("ladder");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 3, "the limit orders of the ladder");
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).Count(order => order.Status == LiveOrderStatus.Active) == 2, "the limits on the exchange");
        OrderRequest buy = _scenario.Exchange.Orders.Single(order => order.Side == OrderSide.Buy && order.Type == OrderType.Limit);
        _scenario.Exchange.Fill(buy.ClientId, buy.Quantity!.Value, buy.Price!.Value, silently: true);

        await _scenario.ActAsync(bot, "stop");

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).Status == LiveBotStatus.Stopped, "the stopped bot");
        (await _scenario.OrdersAsync(bot)).Single(order => order.ClientId.Code == buy.ClientId).Status.ShouldBe(LiveOrderStatus.Filled);
        (await _scenario.PositionsAsync(bot))[0].Quantity.ShouldBe(1m + buy.Quantity.Value);
    }

    [Test]
    public async Task Cancel_the_exchange_refuses_for_good_fails_the_bot()
    {
        Guid bot = await StartedAsync("ladder");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 3, "the limit orders of the ladder");
        _scenario.Exchange.CancelRefusals.Enqueue(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "API key is invalid."));

        await _scenario.ActAsync(bot, "stop");

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(bot)).StopReason == "API key is invalid.", "the reason of the failure");
    }

    // Scenario "Расхождение при сверке": every working bot of the account and the instrument stops.
    [Test]
    public async Task Difference_stops_every_bot_of_the_symbol()
    {
        Guid first = await _scenario.CreateBotAsync();
        Guid second = await _scenario.CreateBotAsync();
        _scenario.StartWorker();
        await _scenario.ActAsync(first, "start");
        await _scenario.ActAsync(second, "start");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "a start order of each bot");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(first, 1), 1m, 100m);
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(second, 1), 1m, 100m);
        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(second)).FirstOrDefault()?.Quantity == 1m, "the fill of the second bot");
        await _scenario.StopWorkerAsync(WorkerOf());
        _scenario.Exchange.Holding = 1m;

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.LiveAsync(first)).Status == LiveBotStatus.Failed && (await _scenario.LiveAsync(second)).Status == LiveBotStatus.Failed, "both bots failed");
    }

    // A fill that lands while the books are compared is no difference: the books are read again before any bot stops.
    [Test]
    public async Task Fill_during_the_comparison_does_not_stop_the_bot()
    {
        Guid bot = await _scenario.CreateBotAsync(category: "linear");
        WorkerHost first = _scenario.StartWorker();
        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).SingleOrDefault()?.Status == LiveOrderStatus.Active, "the start order on the exchange");
        await _scenario.StopWorkerAsync(first);
        _scenario.Exchange.OnHoldingRead(() => _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m));

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the fill");
        await SettleAsync();
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
    }

    [Test]
    public async Task Linear_bot_trades_against_the_position_of_its_symbol()
    {
        Guid bot = await _scenario.CreateBotAsync(category: "linear");
        _scenario.StartWorker();

        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the start order");
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);

        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the open linear position");
        (await _scenario.LiveAsync(bot)).Status.ShouldBe(LiveBotStatus.Running);
    }

    // Scenario "Ответ на отправку потерян": the exchange took the order, its answer never came; the restart finds it there.
    [Test]
    public async Task Order_the_exchange_took_without_an_answer_is_found_after_the_restart()
    {
        Guid bot = await _scenario.CreateBotAsync();
        _scenario.Exchange.HoldAnswer = new TaskCompletionSource();
        WorkerHost first = _scenario.StartWorker();
        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the order on the exchange");
        await _scenario.StopWorkerAsync(first);
        _scenario.Exchange.HoldAnswer = null;

        _scenario.StartWorker();

        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).Single().Status == LiveOrderStatus.Active, "the order known as taken");
        _scenario.Exchange.Sent.Count.ShouldBe(1);
    }

    // Buys 100 when flat, then holds a sell limit at the 3-bar EMA of the close plus 10%.
    private const string EmaLimit = """
        {
          "formatVersion": 1, "direction": "long", "parameters": [],
          "nodes": [
            { "id": "candle", "kind": "candle" },
            { "id": "position", "kind": "position" },
            { "id": "flat", "kind": "not" },
            { "id": "ema", "kind": "ema", "inputs": { "period": 3 } },
            { "id": "target", "kind": "multiply", "inputs": { "b": 1.1 } },
            { "id": "entry", "kind": "market-order", "settings": { "side": "buy", "size": "amount" }, "inputs": { "size": 100 } },
            { "id": "sell", "kind": "hold-limit", "settings": { "side": "sell", "size": "quantity" } }
          ],
          "wires": [
            { "from": "position.hasPosition", "to": "flat.value" },
            { "from": "flat.result", "to": "entry.when" },
            { "from": "candle.close", "to": "ema.source" },
            { "from": "ema.value", "to": "target.a" },
            { "from": "position.hasPosition", "to": "sell.when" },
            { "from": "target.result", "to": "sell.price" },
            { "from": "position.quantity", "to": "sell.size" }
          ],
          "layout": {}
        }
        """;

    private async Task<Guid> StartedAsync(string strategy = "dca")
    {
        Guid bot = await _scenario.CreateBotAsync(strategy);
        _scenario.StartWorker();
        await _scenario.ActAsync(bot, "start");
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the start order");
        await _scenario.WaitAsync(async () => (await _scenario.OrdersAsync(bot)).Single().Status == LiveOrderStatus.Active, "the start order on the exchange");

        return bot;
    }

    private async Task<Guid> StartedAndFilledAsync()
    {
        Guid bot = await StartedAsync();
        _scenario.Exchange.Fill(LiveTradingScenario.ClientId(bot, 1), 1m, 100m);
        await _scenario.WaitAsync(async () => (await _scenario.PositionsAsync(bot)).FirstOrDefault()?.Quantity == 1m, "the filled start order");

        return bot;
    }

    private WorkerHost WorkerOf()
    {
        return _scenario.Workers.Single();
    }

    /// <summary>Lets the Workers take what is queued: a few polls of the clock.</summary>
    private async Task SettleAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            _scenario.Factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50);
        }
    }
}