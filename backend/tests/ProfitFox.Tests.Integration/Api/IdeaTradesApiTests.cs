using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Integration.Workers;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Вход в сделку", "Отмена и закрытие сделки" and "Список сделок".</summary>
public class IdeaTradesApiTests
{
    private const string Url = "/api/idea-trades";

    private LiveTradingScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scenario = await LiveTradingScenario.CreateAsync();
        _scenario.Factory.Bybit.Rules[("linear", "BTCUSDT")] = ("0.01", "0.01", "0.01", "1");
        _scenario.Factory.Bybit.Rules[("spot", "BTCUSDT")] = ("0.01", "0.01", "0.01", "1");
    }

    [TearDown]
    public async Task TearDown()
    {
        await _scenario.DisposeAsync();
    }

    // Scenario "Вход по рынку на linear": the API saves the trade and its entry; it places nothing, a Worker does.
    [Test]
    public async Task Entry_by_the_market_is_saved_with_the_stop_and_the_target_and_not_sent()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");

        Guid id = await CreateAsync(idea);

        IdeaTrade trade = await _scenario.TradeAsync(id);
        (trade.Status, trade.AccountId, trade.EntryPrice, trade.Stop, trade.Target, trade.Quantity, trade.Amount).ShouldBe((IdeaTradeStatus.AwaitingEntry, _scenario.Account, 100m, 98m, 104m, 0.47m, 47.00m));
        trade.Risk.ShouldBe(0.991183m, 0.000001m);
        trade.LastOrderNumber.ShouldBe(1);
        IdeaTradeOrder entry = (await _scenario.TradeOrdersAsync(id)).Single();
        (entry.Number, entry.Role, entry.Side, entry.Type, entry.TimeInForce, entry.Price, entry.Quantity, entry.Protection, entry.ReduceOnly, entry.Status)
            .ShouldBe((1, IdeaTradeOrderRole.Entry, OrderSide.Buy, OrderType.Market, TimeInForce.GoodTilCancelled, null, 0.47m, new Protection(104m, 98m), false, LiveOrderStatus.PendingSubmit));
        _scenario.Exchange.Sent.ShouldBeEmpty();
    }

    // Scenario "Откат".
    [Test]
    public async Task Pullback_enters_with_a_limit_at_the_price_of_its_idea()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m, IdeaType.Pullback);
        _scenario.SetPrice("101");

        IdeaTradeOrder entry = (await _scenario.TradeOrdersAsync(await CreateAsync(idea))).Single();

        (entry.Type, entry.TimeInForce, entry.Price).ShouldBe((OrderType.Limit, TimeInForce.GoodTilCancelled, 100m));
    }

    // Decision 9: the spot is closed in the API until the real check says how its TP/SL orders are told apart.
    [Test]
    public async Task Entry_on_the_spot_is_refused_and_saves_nothing()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 106m, instrument: LiveTradingScenario.Spot);
        _scenario.SetPrice("100.5", LiveTradingScenario.Spot);

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, risk = 1m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["spot_not_supported"]);
        (await ListAsync()).GetArrayLength().ShouldBe(0);
    }

    // A stop entry waits for the price to leave the range of its setup: taken now, it would enter at the market inside the range.
    [Test]
    public async Task Entry_by_a_stop_entry_is_refused_and_saves_nothing()
    {
        Guid idea = await _scenario.SaveIdeaAsync(101m, 98m, 107m, IdeaType.StopEntry);
        _scenario.SetPrice("100");

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, risk = 1m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["stop_entry_not_supported"]);
        (await ListAsync()).GetArrayLength().ShouldBe(0);
    }

    // The limits of open trades and of the total risk count every account, so one lock serialises the entries of all of them.
    [Test]
    public async Task Entries_wait_for_each_other_whatever_the_account()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        await using var database = NpgsqlDataSource.Create(_scenario.Factory.ConnectionString);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await using NpgsqlTransaction other = await connection.BeginTransactionAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('idea-trades:create', 0))", connection, other))
        {
            await take.ExecuteNonQueryAsync();
        }

        Task<HttpResponseMessage> entry = _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, risk = 1m });
        await Task.Delay(500);

        entry.IsCompleted.ShouldBeFalse();
        await other.CommitAsync();
        (await entry).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Test]
    public async Task Short_idea_sells_to_enter()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 102m, 96m, side: IdeaSide.Short);
        _scenario.SetPrice("100");

        IdeaTrade trade = await _scenario.TradeAsync(await CreateAsync(idea));

        trade.Side.ShouldBe(IdeaSide.Short);
        (await _scenario.TradeOrdersAsync(trade.Id)).Single().Side.ShouldBe(OrderSide.Sell);
    }

    // The checks of the entry are made again at the entry: the reason comes with a 409 and no trade is saved.
    [Test]
    public async Task Entry_with_a_reason_is_refused_with_it_and_saves_nothing()
    {
        Guid idea = await _scenario.SaveIdeaAsync(23.48m, 23.01m, 24.42m);
        _scenario.SetPrice("24.50");

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, risk = 1m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldContain("price_beyond_target");
        (await ListAsync()).GetArrayLength().ShouldBe(0);
    }

    // Scenario "Лимит открытых сделок": the second entry sees the first one under the lock of the account.
    [Test]
    public async Task Second_entry_sees_the_first_and_the_limit_of_open_trades()
    {
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = _scenario.Account, risk = 1m, maxAmount = 200m, maxOpenTrades = 1, maxTotalRisk = 5m, minRiskReward = 1.5m });
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        await CreateAsync(idea);

        HttpResponseMessage second = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["too_many_open_trades"]);
    }

    [Test]
    public async Task Unknown_idea_is_not_found_and_an_unknown_account_is_refused_by_its_field()
    {
        (await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = Guid.NewGuid() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["accountId"]);
    }

    // Scenario "Сделка в списке".
    [Test]
    public async Task List_gives_the_newest_trade_first_with_its_idea_and_its_numbers_as_strings()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        Guid first = await CreateAsync(idea);
        _scenario.Factory.Time.Advance(TimeSpan.FromMinutes(1));
        Guid second = await CreateAsync(idea, risk: "0.5");

        JsonElement list = await ListAsync();

        list.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([second, first]);
        JsonElement item = list[1];
        item.GetProperty("category").GetString().ShouldBe("linear");
        item.GetProperty("symbol").GetString().ShouldBe("BTCUSDT");
        item.GetProperty("setup").GetString().ShouldBe("impulse");
        item.GetProperty("interval").GetString().ShouldBe("15");
        item.GetProperty("type").GetString().ShouldBe("target_2r");
        item.GetProperty("side").GetString().ShouldBe("long");
        item.GetProperty("accountId").GetGuid().ShouldBe(_scenario.Account);
        item.GetProperty("accountName").GetString().ShouldBe("Основной");
        item.GetProperty("status").GetString().ShouldBe("awaiting_entry");
        item.GetProperty("entryPrice").GetString().ShouldBe("100");
        item.GetProperty("stop").GetString().ShouldBe("98");
        item.GetProperty("target").GetString().ShouldBe("104");
        item.GetProperty("quantity").GetString().ShouldBe("0.47");
        item.GetProperty("amount").GetString().ShouldBe("47.00");
        item.GetProperty("result").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Closed_trade_in_the_list_has_its_result_in_money_and_in_r()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(risk: 0.2m);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");
        _scenario.Exchange.TriggerChild(entry, StopOrderKind.TakeProfit, 115m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtTarget, "the target");

        JsonElement item = (await ListAsync())[0];

        item.GetProperty("status").GetString().ShouldBe("closed_at_target");
        item.GetProperty("result").GetString().ShouldBe("4.68");
        item.GetProperty("resultR").GetString().ShouldBe("23.4");
    }

    // Scenario "Отменить": the API asks, the Worker takes the entry off.
    [Test]
    public async Task Cancel_of_a_trade_that_waits_takes_the_entry_off_through_the_worker()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m, IdeaType.Pullback);
        _scenario.SetPrice("100");
        Guid id = await CreateAsync(idea);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the limit");

        (await _scenario.Client.PostAsync($"{Url}/{id}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(id)).Status == IdeaTradeStatus.Cancelled, "the cancelled trade");
        _scenario.Exchange.Cancels.ShouldBe([new IdeaTradeOrderId(id, 1).Code]);
    }

    // Scenario "Закрыть по рынку".
    [Test]
    public async Task Close_of_an_open_trade_sells_it_through_the_worker()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        Guid id = await CreateAsync(idea);
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(new IdeaTradeOrderId(id, 1).Code, 0.47m, 100m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(id)).Count == 2, "the stop and the target");

        (await _scenario.Client.PostAsync($"{Url}/{id}/close", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 2, "the close");
        _scenario.Exchange.Fill(new IdeaTradeOrderId(id, 2).Code, 0.47m, 101m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(id)).Status == IdeaTradeStatus.ClosedManually, "the closed trade");
    }

    [Test]
    public async Task Cancel_and_close_are_refused_when_the_status_does_not_allow_them()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        Guid waiting = await CreateAsync(idea);

        (await _scenario.Client.PostAsync($"{Url}/{waiting}/close", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await _scenario.ChangeTradeAsync(waiting, trade => trade.EntryGrown(new LegTotals(0.47m, 47m, 0m), _scenario.Factory.Time.GetUtcNow()));
        (await _scenario.Client.PostAsync($"{Url}/{waiting}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _scenario.Client.PostAsync($"{Url}/{Guid.NewGuid()}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _scenario.Client.PostAsync($"{Url}/{Guid.NewGuid()}/close", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Guid> CreateAsync(Guid idea, string risk = "1")
    {
        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, risk = decimal.Parse(risk, System.Globalization.CultureInfo.InvariantCulture) });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ListAsync()
    {
        HttpResponseMessage response = await _scenario.Client.GetAsync(Url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}