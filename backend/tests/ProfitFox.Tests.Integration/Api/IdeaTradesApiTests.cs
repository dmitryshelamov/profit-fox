using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using NSubstitute;

using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Integration.Workers;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Вход в сделку", "Отмена и закрытие сделки" and "Чтение журнала".</summary>
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
        trade.Risk.ShouldBe(1.037713m, 0.000001m);
        trade.LastOrderNumber.ShouldBe(1);
        IdeaTradeOrder entry = (await _scenario.TradeOrdersAsync(id)).Single();
        (entry.Number, entry.Role, entry.Side, entry.Type, entry.TimeInForce, entry.Price, entry.Quantity, entry.Protection, entry.ReduceOnly, entry.Status)
            .ShouldBe((1, IdeaTradeOrderRole.Entry, OrderSide.Buy, OrderType.Market, TimeInForce.GoodTilCancelled, null, 0.47m, new Protection(104m, 98m), false, LiveOrderStatus.PendingSubmit));
        _scenario.Exchange.Sent.ShouldBeEmpty();
    }

    // Requirement "Обещание на входе", scenario "Вход на ▲": the promise is the statistics of the bucket as «Идеи» shows them, with the break-even and the
    // reward to risk after the fees and the slippage from the price the trade entered at (101, not the 100 of the idea).
    [Test]
    public async Task Entry_on_an_up_badge_keeps_the_promise_of_the_bucket_and_a_recount_of_the_bucket_does_not_change_it()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 96m, 112m);
        _scenario.SetPrice("101");
        await _scenario.SaveBucketAsync(new BucketStatistics(412, 130, 270, 12, 0.31m, 0.27m, 0.36m, 0.14m, 0.04m, 0.24m));

        Guid id = await CreateAsync(idea);

        IdeaShares shares = IdeaMoney.PerUsdt(new IdeaPlan(IdeaType.TwoR, IdeaSide.Long, 101m, 96m, 112m), new FeeRates(0.02m, 0.055m, 0.05m));
        var promise = new IdeaPromise(IdeaBadge.Up, 0.31m, 0.14m, 412, shares.BreakEven, shares.NetRiskReward);
        (await _scenario.TradeAsync(id)).Promise.ShouldBe(promise);
        await _scenario.SaveBucketAsync(new BucketStatistics(450, 140, 290, 20, 0.31m, 0.27m, 0.36m, 0.01m, -0.05m, 0.07m));
        (await _scenario.TradeAsync(id)).Promise.ShouldBe(promise);
    }

    // Scenario "Мало данных": no badge and no probability, the cases and the expectancy stay.
    [Test]
    public async Task Entry_on_a_bucket_with_too_few_cases_keeps_its_cases_and_expectancy_only()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 96m, 112m);
        _scenario.SetPrice("101");
        await _scenario.SaveBucketAsync(new BucketStatistics(12, 4, 8, 0, null, null, null, 0.05m, null, null));

        IdeaPromise promise = (await _scenario.TradeAsync(await CreateAsync(idea))).Promise!;

        (promise.Badge, promise.Probability, promise.Expectancy, promise.Cases).ShouldBe((null, null, 0.05m, 12));
    }

    [Test]
    public async Task Entry_on_an_idea_without_a_bucket_keeps_the_break_even_and_the_reward_to_risk_only()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 96m, 112m);
        _scenario.SetPrice("101");

        IdeaPromise promise = (await _scenario.TradeAsync(await CreateAsync(idea))).Promise!;

        (promise.Badge, promise.Probability, promise.Expectancy, promise.Cases).ShouldBe((null, null, null, null));
        promise.NetRiskReward.ShouldBeGreaterThan(1m);
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

    // Scenario "Вход на споте": a limit IOC at the current price that carries the stop and the target of the idea.
    [Test]
    public async Task Entry_on_the_spot_is_a_limit_ioc_with_its_stop_and_target()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 106m, instrument: LiveTradingScenario.Spot);
        _scenario.SetPrice("100.5", LiveTradingScenario.Spot);

        IdeaTradeOrder entry = (await _scenario.TradeOrdersAsync(await CreateAsync(idea))).Single();

        (entry.Type, entry.TimeInForce, entry.Price, entry.Protection).ShouldBe((OrderType.Limit, TimeInForce.ImmediateOrCancel, 100.5m, new Protection(106m, 98m)));
    }

    // A stop entry waits for the price to leave the range of its setup: taken now, it would enter at the market inside the range.
    [Test]
    public async Task Entry_by_a_stop_entry_is_refused_and_saves_nothing()
    {
        Guid idea = await _scenario.SaveIdeaAsync(101m, 98m, 107m, IdeaType.StopEntry);
        _scenario.SetPrice("100");

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, amount = 47.3m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["stop_entry_not_supported"]);
        (await NothingSavedAsync()).ShouldBeTrue();
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

        Task<HttpResponseMessage> entry = _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, amount = 47.3m });
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

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, amount = 47.3m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonElement problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldContain("price_beyond_target");
        (await NothingSavedAsync()).ShouldBeTrue();
    }

    // Scenario "Символ с плечом 10×": the API sets the leverage of the settings on the empty symbol, and the trade keeps it.
    [Test]
    public async Task Entry_on_linear_sets_the_leverage_of_an_empty_symbol_and_the_trade_keeps_it()
    {
        _scenario.Factory.Ledger.GetLeverageAsync(Arg.Any<ExchangeKey>(), LiveTradingScenario.Linear, Arg.Any<CancellationToken>()).Returns(10m);
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");

        Guid id = await CreateAsync(idea);

        await _scenario.Factory.Orders.Received(1).SetLeverageAsync(Arg.Any<ExchangeKey>(), LiveTradingScenario.Linear, 1, Arg.Any<CancellationToken>());
        (await _scenario.TradeAsync(id)).Leverage.ShouldBe(1);
    }

    // Scenario "Bybit отказал": a 409 with the reason, and no trade.
    [Test]
    public async Task Entry_whose_leverage_bybit_refuses_is_refused_with_the_reason_and_saves_nothing()
    {
        _scenario.Factory.Ledger.GetLeverageAsync(Arg.Any<ExchangeKey>(), LiveTradingScenario.Linear, Arg.Any<CancellationToken>()).Returns(10m);
        _scenario.Factory.Orders.SetLeverageAsync(Arg.Any<ExchangeKey>(), LiveTradingScenario.Linear, 1, Arg.Any<CancellationToken>())
            .Returns(new OrderOutcome(OrderOutcomeKind.Permanent, Reason: "leverage invalid"));
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, amount = 47.3m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["leverage_not_set"]);
        (await NothingSavedAsync()).ShouldBeTrue();
    }

    // Scenario "Лимит открытых сделок": the second entry sees the first one under the lock of the account.
    [Test]
    public async Task Second_entry_sees_the_first_and_the_limit_of_open_trades()
    {
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = _scenario.Account, amount = 47.3m, maxAmount = 200m, maxOpenTrades = 1, maxTotalRisk = 5m, minRiskReward = 1.5m });
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        await CreateAsync(idea);

        HttpResponseMessage second = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, amount = 47.3m });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["too_many_open_trades"]);
    }

    [Test]
    public async Task Unknown_idea_is_not_found_and_an_unknown_account_is_refused_by_its_field()
    {
        (await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = Guid.NewGuid(), amount = 47.3m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = Guid.NewGuid(), amount = 47.3m });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["accountId"]);
    }

    // The amount is required: a body without it creates nothing.
    [Test]
    public async Task Entry_without_an_amount_is_refused_by_its_field_and_saves_nothing()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["amount"]);
        (await NothingSavedAsync()).ShouldBeTrue();
    }

    // Scenario "Сумма упёрлась в предел": the amount above the limit is not cut, the entry is refused.
    [Test]
    public async Task Entry_above_the_largest_amount_is_refused_and_saves_nothing()
    {
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = _scenario.Account, amount = 47.3m, maxAmount = 100m, maxOpenTrades = 5, maxTotalRisk = 5m, minRiskReward = 1.5m });
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");

        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, amount = 100.5m });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()).ShouldBe(["amount_above_max"]);
        (await NothingSavedAsync()).ShouldBeTrue();
    }

    // Scenario "Сделки в работе и закрытые": the open trades newest first with their idea and their numbers as strings, nothing closed yet.
    [Test]
    public async Task Journal_gives_the_open_trades_newest_first_with_their_idea_and_their_numbers_as_strings()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        Guid first = await CreateAsync(idea);
        _scenario.Factory.Time.Advance(TimeSpan.FromMinutes(1));
        Guid second = await CreateAsync(idea, amount: "23.7");

        JsonElement journal = await JournalAsync();

        JsonElement open = journal.GetProperty("open");
        open.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([second, first]);
        JsonElement item = open[1];
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
        foreach (string absent in new[] { "openedAt", "secondsToOutcome", "ideaOutcome", "ideaEndsAt", "lastPrice", "currentResult", "currentResultR" })
        {
            item.GetProperty(absent).ValueKind.ShouldBe(JsonValueKind.Null, absent);
        }

        // A trade made now promises its break-even and reward to risk, though its idea has no bucket.
        item.GetProperty("promise").GetProperty("badge").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("features").GetArrayLength().ShouldBe(0);

        JsonElement closed = journal.GetProperty("closed");
        (closed.GetProperty("items").GetArrayLength(), closed.GetProperty("page").GetInt32(), closed.GetProperty("pageSize").GetInt32(), closed.GetProperty("total").GetInt32()).ShouldBe((0, 1, 50, 0));
        JsonElement totals = journal.GetProperty("totals");
        (totals.GetProperty("targets").GetInt32(), totals.GetProperty("result").GetString(), totals.GetProperty("fees").GetString()).ShouldBe((0, "0", "0"));
        foreach (string absent in new[] { "meanR", "meanRLow", "meanRHigh", "meanSecondsToOutcome" })
        {
            totals.GetProperty(absent).ValueKind.ShouldBe(JsonValueKind.Null, absent);
        }

        (totals.GetProperty("promise").GetProperty("count").GetInt32(), totals.GetProperty("ideas").GetProperty("count").GetInt32()).ShouldBe((0, 0));
    }

    // Scenarios "Закрытая по стопу" and "Итоги по фильтру": a closed trade has its time to the outcome, its result, and the totals are over it.
    [Test]
    public async Task Closed_trade_in_the_journal_has_its_result_in_money_and_in_r_and_its_time_to_the_outcome()
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync(risk: 0.2m);
        string entry = new IdeaTradeOrderId(trade.Id, 1).Code;
        _scenario.StartWorker();
        await _scenario.WaitAsync(() => _scenario.Exchange.Orders.Count == 1, "the entry");
        _scenario.Exchange.Fill(entry, 0.47m, 105m, fee: 0.02m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeChildrenAsync(trade.Id)).Count == 2, "the stop and the target");
        _scenario.Factory.Time.Advance(TimeSpan.FromMinutes(90));
        _scenario.Exchange.TriggerChild(entry, StopOrderKind.TakeProfit, 115m);
        await _scenario.WaitAsync(async () => (await _scenario.TradeAsync(trade.Id)).Status == IdeaTradeStatus.ClosedAtTarget, "the target");

        JsonElement journal = await JournalAsync();

        journal.GetProperty("open").GetArrayLength().ShouldBe(0);
        JsonElement item = journal.GetProperty("closed").GetProperty("items")[0];
        item.GetProperty("status").GetString().ShouldBe("closed_at_target");
        item.GetProperty("result").GetString().ShouldBe("4.68");
        item.GetProperty("resultR").GetString().ShouldBe("23.4");
        item.GetProperty("openedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        item.GetProperty("secondsToOutcome").GetInt64().ShouldBeInRange(5400, 5460);
        JsonElement totals = journal.GetProperty("totals");
        (totals.GetProperty("targets").GetInt32(), totals.GetProperty("result").GetString(), totals.GetProperty("meanR").GetString()).ShouldBe((1, "4.68", "23.4"));
        totals.GetProperty("meanSecondsToOutcome").GetInt64().ShouldBeInRange(5400, 5460);
        totals.GetProperty("meanRLow").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Requirements "Обещание на входе" and "Исход идеи на сделке" reach the journal: the promise of the entry and the outcome the scanner wrote.
    [Test]
    public async Task Journal_carries_the_promise_of_the_entry_and_the_outcome_of_the_idea()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 96m, 112m);
        _scenario.SetPrice("101");
        await _scenario.SaveBucketAsync(new BucketStatistics(412, 130, 270, 12, 0.31m, 0.27m, 0.36m, 0.14m, 0.04m, 0.24m));
        Guid id = await CreateAsync(idea);
        await _scenario.WriteIdeaOutcomeAsync(idea, new TradedIdeaOutcome(IdeaStatus.Target, _scenario.Factory.Time.GetUtcNow(), 1.84m));

        JsonElement item = (await JournalAsync()).GetProperty("open")[0];

        item.GetProperty("id").GetGuid().ShouldBe(id);
        JsonElement promise = item.GetProperty("promise");
        (promise.GetProperty("badge").GetString(), promise.GetProperty("probability").GetString(), promise.GetProperty("expectancy").GetString(), promise.GetProperty("cases").GetInt32()).ShouldBe(("up", "0.31", "0.14", 412));
        promise.GetProperty("breakEven").GetString().ShouldNotBeNull();
        promise.GetProperty("netRiskReward").GetString().ShouldNotBeNull();
        JsonElement outcome = item.GetProperty("ideaOutcome");
        (outcome.GetProperty("status").GetString(), outcome.GetProperty("result").GetString()).ShouldBe(("target", "1.84"));
    }

    // Requirement "Конец идеи у сделки в работе": a trade in work gives the end of the lifetime of its idea once the idea is active.
    [Test]
    public async Task Trade_in_work_gives_the_end_of_the_lifetime_of_its_active_idea()
    {
        Guid ideaId = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        Guid id = await CreateAsync(ideaId);
        await using var database = NpgsqlDataSource.Create(_scenario.Factory.ConnectionString);
        var store = new IdeaRepository(database);
        Idea idea = (await store.GetAsync(ideaId, CancellationToken.None))!;
        DateTimeOffset entered = _scenario.Factory.Time.GetUtcNow();
        Idea active = idea with { Track = idea.Track with { Status = IdeaStatus.Active, StatusAt = entered, EnteredAt = entered } };
        await store.SaveAsync([], [active], CancellationToken.None);

        JsonElement item = (await JournalAsync()).GetProperty("open")[0];

        item.GetProperty("id").GetGuid().ShouldBe(id);
        item.GetProperty("ideaEndsAt").GetDateTimeOffset().ShouldBe(active.Track.LifeEndsAt!.Value);
    }

    // Task 6.2 of add-market-idea-features: a real trade gives the features its idea fired with.
    [Test]
    public async Task Journal_gives_the_features_of_the_idea_of_a_trade()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m, funding: 0.0006m, burstPairs: 25);
        _scenario.SetPrice("100");
        await CreateAsync(idea);

        JsonElement item = (await JournalAsync()).GetProperty("open")[0];

        // Scenario "Сделка со всплеском" of add-burst-and-auto-limits: the burst its idea fired in, too.
        item.GetProperty("features").EnumerateArray().Select(feature => feature.GetString()).ShouldBe(["high_funding", "burst"]);
    }

    // Scenario "Открытая в плюсе": the last price of the exchange gives the current result of an open trade.
    [Test]
    public async Task Open_trade_has_the_last_price_and_its_current_result()
    {
        Guid idea = await _scenario.SaveIdeaAsync(100m, 98m, 104m);
        _scenario.SetPrice("100");
        Guid id = await CreateAsync(idea);
        await _scenario.ChangeTradeAsync(id, trade => trade.EntryGrown(new LegTotals(0.47m, 47m, 0m), _scenario.Factory.Time.GetUtcNow()));
        _scenario.SetPrice("101");

        JsonElement item = (await JournalAsync()).GetProperty("open")[0];

        item.GetProperty("lastPrice").GetString().ShouldBe("101");
        item.GetProperty("currentResult").GetString().ShouldBe("0.4438915");
        item.GetProperty("currentResultR").ValueKind.ShouldBe(JsonValueKind.String);
    }

    // Scenarios "Неизвестный размер страницы" and the filters: the page, the filter by the symbol and the badge, the errors by the name of the parameter.
    [Test]
    public async Task Journal_takes_a_page_and_filters_and_refuses_a_wrong_size()
    {
        foreach (int minutes in new[] { 1, 2, 3 })
        {
            IdeaTrade trade = await _scenario.CreateTradeAsync(risk: 0.2m);
            DateTimeOffset now = _scenario.Factory.Time.GetUtcNow();
            await _scenario.ChangeTradeAsync(
                trade.Id,
                current => current.EntryGrown(new LegTotals(0.47m, 49.35m, 0m), now).ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(0.47m, 46m, 0m), now.AddMinutes(minutes)));
        }

        JsonElement all = (await JournalAsync()).GetProperty("closed");
        JsonElement other = (await JournalAsync("?symbol=eth")).GetProperty("closed");
        JsonElement withoutBadge = (await JournalAsync("?badge=none&days=7&page=1&pageSize=100")).GetProperty("closed");
        HttpResponseMessage wrong = await _scenario.Client.GetAsync($"{Url}?pageSize=75");

        (all.GetProperty("total").GetInt32(), all.GetProperty("items").GetArrayLength()).ShouldBe((3, 3));
        other.GetProperty("total").GetInt32().ShouldBe(0);
        (withoutBadge.GetProperty("total").GetInt32(), withoutBadge.GetProperty("pageSize").GetInt32()).ShouldBe((3, 100));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await wrong.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["pageSize"]);
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

        // A trade that never opened has no time to its outcome; the idea of a pullback that expired without an entry has no result.
        await _scenario.WriteIdeaOutcomeAsync(idea, new TradedIdeaOutcome(IdeaStatus.Expired, _scenario.Factory.Time.GetUtcNow(), null));
        JsonElement cancelled = (await JournalAsync()).GetProperty("closed").GetProperty("items")[0];
        cancelled.GetProperty("secondsToOutcome").ValueKind.ShouldBe(JsonValueKind.Null);
        (cancelled.GetProperty("ideaOutcome").GetProperty("status").GetString(), cancelled.GetProperty("ideaOutcome").GetProperty("result").ValueKind).ShouldBe(("expired", JsonValueKind.Null));
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

    private async Task<Guid> CreateAsync(Guid idea, string amount = "47.3")
    {
        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, new { ideaId = idea, accountId = _scenario.Account, amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> JournalAsync(string query = "")
    {
        HttpResponseMessage response = await _scenario.Client.GetAsync(Url + query);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private async Task<bool> NothingSavedAsync()
    {
        JsonElement journal = await JournalAsync();

        return journal.GetProperty("open").GetArrayLength() == 0 && journal.GetProperty("closed").GetProperty("total").GetInt32() == 0;
    }
}