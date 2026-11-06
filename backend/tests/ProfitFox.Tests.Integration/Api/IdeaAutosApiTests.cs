using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Tests.Integration.Workers;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements of the autos: "Автомат", "Проверка автомата", "Порядок автоматов", "Режим автомата", "Удаление автомата", "Чтение отказов" and "Итоги автомата".</summary>
public class IdeaAutosApiTests
{
    private const string Url = "/api/idea-autos";

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

    // Scenario "Новый автомат": the third of three, switched off.
    [Test]
    public async Task New_auto_is_created_at_the_end_of_the_list_switched_off()
    {
        await CreateAsync("А");
        await CreateAsync("Б");

        Guid id = await CreateAsync("Импульс лонг");

        JsonElement auto = await GetAsync(id);
        (auto.GetProperty("position").GetInt32(), auto.GetProperty("mode").GetString()).ShouldBe((2, "off"));
        (await ListAsync()).Select(item => item.GetProperty("auto").GetProperty("name").GetString()).ShouldBe(["А", "Б", "Импульс лонг"]);
    }

    // Scenario "Чтение автомата".
    [Test]
    public async Task Auto_is_read_with_the_name_the_account_the_amount_the_mode_every_condition_and_the_place()
    {
        Guid id = await CreateAsync("Импульс лонг", amount: 120.5m, side: "long", category: "linear", badge: "flat", minRiskReward: 1.35m, minCases: 100);

        JsonElement auto = await GetAsync(id);

        auto.GetProperty("name").GetString().ShouldBe("Импульс лонг");
        (auto.GetProperty("accountId").GetGuid(), auto.GetProperty("accountName").GetString(), Number(auto.GetProperty("amount"))).ShouldBe((_scenario.Account, "Основной", 120.5m));
        auto.GetProperty("setups").EnumerateArray().Select(item => item.GetString()).ShouldBe(["impulse", "breakout"]);
        auto.GetProperty("types").EnumerateArray().Select(item => item.GetString()).ShouldBe(["target_2r", "pullback"]);
        auto.GetProperty("intervals").EnumerateArray().Select(item => item.GetString()).ShouldBe(["15", "60"]);
        (auto.GetProperty("side").GetString(), auto.GetProperty("category").GetString(), auto.GetProperty("badge").GetString()).ShouldBe(("long", "linear", "flat"));
        (Number(auto.GetProperty("minRiskReward")), auto.GetProperty("minCases").GetInt32()).ShouldBe((1.35m, 100));
    }

    [Test]
    public async Task Conditions_left_open_are_read_back_as_none()
    {
        Guid id = await CreateAsync("Все");

        JsonElement auto = await GetAsync(id);

        (auto.GetProperty("side").ValueKind, auto.GetProperty("category").ValueKind, auto.GetProperty("badge").ValueKind).ShouldBe((JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null));
    }

    [Test]
    public async Task Auto_that_is_not_there_is_a_404()
    {
        (await _scenario.Client.GetAsync($"{Url}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _scenario.Client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", Body("А"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _scenario.Client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}/mode", new { mode = "paper" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _scenario.Client.DeleteAsync($"{Url}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Scenario "Сумма выше предела": the largest amount 500, the auto asks 600.
    [Test]
    public async Task Every_wrong_field_is_a_400_on_its_own_name()
    {
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = (Guid?)null, amount = 20m, maxAmount = 500m, maxOpenTrades = 5, maxTotalRisk = 5m, minRiskReward = 1.5m });
        await CreateAsync("Пробои 5м");

        await RefusedOnAsync(Body("Пробои 5м"), "name");
        await RefusedOnAsync(Body(""), "name");
        await RefusedOnAsync(Body(new string('я', 61)), "name");
        await RefusedOnAsync(Body("А", accountId: Guid.NewGuid()), "accountId");
        await RefusedOnAsync(Body("А", amount: 600m), "amount");
        await RefusedOnAsync(Body("А", amount: 0m), "amount");
        await RefusedOnAsync(Body("А", setups: []), "setups");
        await RefusedOnAsync(Body("А", types: []), "types");
        await RefusedOnAsync(Body("А", intervals: []), "intervals");
        await RefusedOnAsync(Body("А", setups: ["nothing"]), "setups");
        await RefusedOnAsync(Body("А", intervals: ["1"]), "intervals");
        await RefusedOnAsync(Body("А", side: "up"), "side");
        await RefusedOnAsync(Body("А", category: "inverse"), "category");
        await RefusedOnAsync(Body("А", badge: "sideways"), "badge");
        await RefusedOnAsync(Body("А", minRiskReward: -0.1m), "minRiskReward");
        await RefusedOnAsync(Body("А", minCases: -1), "minCases");
        (await ListAsync()).Count.ShouldBe(1);
    }

    // Scenario "Стоп-вход в типах".
    [Test]
    public async Task Stop_entry_among_the_types_is_a_400_on_the_types()
    {
        await RefusedOnAsync(Body("А", types: ["target_2r", "stop_entry"]), "types");
    }

    [Test]
    public async Task Save_writes_the_fields_and_keeps_the_mode_and_the_place_and_the_own_name_is_not_taken()
    {
        await CreateAsync("А");
        Guid id = await CreateAsync("Б");
        await _scenario.Client.PutAsJsonAsync($"{Url}/{id}/mode", new { mode = "paper" });

        HttpResponseMessage saved = await _scenario.Client.PutAsJsonAsync($"{Url}/{id}", Body("Б", amount: 75m, side: "short", minCases: 20));

        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement auto = await GetAsync(id);
        (Number(auto.GetProperty("amount")), auto.GetProperty("side").GetString(), auto.GetProperty("minCases").GetInt32(), auto.GetProperty("mode").GetString(), auto.GetProperty("position").GetInt32())
            .ShouldBe((75m, "short", 20, "paper", 1));
        await RefusedOnAsync(Body("А"), "name", id);
    }

    // Scenario "Учебный в реальный": the mode changes at once, with no question.
    [Test]
    public async Task Mode_changes_at_once_and_an_unknown_mode_is_a_400()
    {
        Guid id = await CreateAsync("А");

        (await _scenario.Client.PutAsJsonAsync($"{Url}/{id}/mode", new { mode = "live" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(id)).GetProperty("mode").GetString().ShouldBe("live");

        HttpResponseMessage refused = await _scenario.Client.PutAsJsonAsync($"{Url}/{id}/mode", new { mode = "demo" });
        await ShouldBeRefusedOnAsync(refused, "mode");
    }

    // Scenario "Перестановка": «А», «Б», «В» become «В», «А», «Б».
    [Test]
    public async Task Order_puts_the_autos_in_the_order_of_the_ids()
    {
        Guid first = await CreateAsync("А");
        Guid second = await CreateAsync("Б");
        Guid third = await CreateAsync("В");

        (await _scenario.Client.PutAsJsonAsync($"{Url}/order", new { ids = new[] { third, first, second } })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync()).Select(item => item.GetProperty("auto").GetProperty("name").GetString()).ShouldBe(["В", "А", "Б"]);
    }

    [Test]
    public async Task Order_that_misses_an_auto_or_holds_a_stranger_or_a_repeat_is_a_400()
    {
        Guid first = await CreateAsync("А");
        Guid second = await CreateAsync("Б");

        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync($"{Url}/order", new { ids = new[] { first } }), "ids");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync($"{Url}/order", new { ids = new[] { first, Guid.NewGuid() } }), "ids");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync($"{Url}/order", new { ids = new[] { first, first } }), "ids");
        (await ListAsync()).Select(item => item.GetProperty("auto").GetProperty("id").GetGuid()).ShouldBe([first, second]);
    }

    // Scenario "Удаление с учебной сделкой": the auto leaves the list, its name is free, and its trade goes on in the journal with its name.
    [Test]
    public async Task Deleted_auto_leaves_the_list_its_name_is_free_again_and_its_trade_keeps_its_name()
    {
        Guid id = await CreateAsync("Пробои 5м");
        await InsertPaperTradeAsync(id, "open", closedAt: null, resultR: null);

        (await _scenario.Client.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync()).ShouldBeEmpty();
        (await _scenario.Client.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _scenario.Client.PostAsJsonAsync(Url, Body("Пробои 5м"))).StatusCode.ShouldBe(HttpStatusCode.Created);
        JsonElement trade = (await JournalAsync()).GetProperty("open").EnumerateArray().Single();
        (trade.GetProperty("origin").GetString(), trade.GetProperty("autoName").GetString(), trade.GetProperty("autoId").GetGuid()).ShouldBe(("paper", "Пробои 5м", id));
    }

    // Scenario "Учебный автомат": 3 closed paper trades with +1.5, +2.0 and −0.5 R, no real ones, two refusals today.
    [Test]
    public async Task List_gives_the_totals_of_the_paper_and_the_real_trades_and_the_refusals_of_today()
    {
        Guid id = await CreateAsync("Пробои 5м");
        await InsertPaperTradeAsync(id, "closed_at_target", Now.AddHours(-3), 1.5m);
        await InsertPaperTradeAsync(id, "closed_at_target", Now.AddHours(-2), 2.0m);
        await InsertPaperTradeAsync(id, "closed_at_stop", Now.AddHours(-1), -0.5m);
        await InsertPaperTradeAsync(id, "open", null, null);
        await InsertRefusalAsync(id, Now.AddHours(-1), "not_enough_money");
        await InsertRefusalAsync(id, Now.AddHours(-2), "daily_stop");
        await InsertRefusalAsync(id, Now.AddDays(-1), "daily_stop");

        JsonElement item = (await ListAsync()).Single();

        (item.GetProperty("paperClosed").GetInt32(), Number(item.GetProperty("paperMeanR")), Number(item.GetProperty("paperSumR"))).ShouldBe((3, 1m, 3m));
        (item.GetProperty("realClosed").GetInt32(), Number(item.GetProperty("realResult")), item.GetProperty("refusalsToday").GetInt32()).ShouldBe((0, 0m, 2));
    }

    // Scenario "Сегодня по автомату": the newest first, one auto, since 00:00 UTC.
    [Test]
    public async Task Refusals_come_from_the_newest_for_one_auto_since_the_start_of_the_day()
    {
        Guid mine = await CreateAsync("Пробои 5м");
        Guid other = await CreateAsync("Падения шорт");
        await InsertRefusalAsync(mine, Now.AddHours(-3), "daily_stop");
        await InsertRefusalAsync(mine, Now.AddHours(-1), "not_enough_money", detail: """{"Actual": 212.40, "Limit": 300, "Holder": null}""");
        await InsertRefusalAsync(other, Now.AddHours(-2), "pair_taken");
        await InsertRefusalAsync(mine, Now.AddDays(-1), "daily_stop");

        JsonElement page = await RefusalsAsync("period=today&autoId=" + mine);

        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("reasons")[0].GetString()).ShouldBe(["not_enough_money", "daily_stop"]);
        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32(), page.GetProperty("total").GetInt32()).ShouldBe((1, 50, 2));
        JsonElement first = page.GetProperty("items")[0];
        (first.GetProperty("autoName").GetString(), first.GetProperty("mode").GetString(), first.GetProperty("symbol").GetString(), Number(first.GetProperty("detail").GetProperty("actual"))).ShouldBe(("Пробои 5м", "live", "SUIUSDT", 212.4m));
        (await RefusalsAsync("period=7d")).GetProperty("total").GetInt32().ShouldBe(4);
    }

    [Test]
    public async Task Refusals_page_past_the_end_is_the_last_one()
    {
        Guid id = await CreateAsync("Пробои 5м");

        for (var index = 0; index < 55; index++)
        {
            await InsertRefusalAsync(id, Now.AddMinutes(-index), "daily_stop");
        }

        JsonElement page = await RefusalsAsync("period=today&page=9");

        (page.GetProperty("page").GetInt32(), page.GetProperty("items").GetArrayLength(), page.GetProperty("total").GetInt32()).ShouldBe((2, 5, 55));
    }

    [Test]
    public async Task Refusals_of_an_unknown_period_or_auto_or_page_are_a_400_on_the_name_of_the_parameter()
    {
        await ShouldBeRefusedOnAsync(await _scenario.Client.GetAsync($"{Url}/refusals?period=month"), "period");
        await ShouldBeRefusedOnAsync(await _scenario.Client.GetAsync($"{Url}/refusals"), "period");
        await ShouldBeRefusedOnAsync(await _scenario.Client.GetAsync($"{Url}/refusals?period=today&autoId={Guid.NewGuid()}"), "autoId");
        await ShouldBeRefusedOnAsync(await _scenario.Client.GetAsync($"{Url}/refusals?period=today&pageSize=75"), "pageSize");
    }

    // Scenarios "Стоп сработал" and "Учебные не в состоянии": −30.15 at 15:12, two real trades open, four paper ones.
    [Test]
    public async Task Guards_tell_the_open_trades_the_loss_of_the_day_and_the_time_the_daily_stop_was_reached()
    {
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = (Guid?)null, amount = 20m, maxAmount = 200m, maxOpenTrades = 8, maxTotalRisk = 40m, minRiskReward = 1.5m, dailyLossLimit = 30m, maxSameDirection = 5 });
        await ClosedTradeAsync(-10m, Now.AddHours(-3));
        await ClosedTradeAsync(-10m, Now.AddHours(-2));
        DateTimeOffset reached = Now.AddHours(-1);
        await ClosedTradeAsync(-10.15m, reached);
        await _scenario.CreateTradeAsync();
        await _scenario.CreateTradeAsync(side: IdeaSide.Short);
        await _scenario.CreateTradeAsync(side: IdeaSide.Long);
        Guid auto = await CreateAsync("Пробои 5м");

        for (var index = 0; index < 4; index++)
        {
            await InsertPaperTradeAsync(auto, "open", null, null);
        }

        HttpResponseMessage response = await _scenario.Client.GetAsync("/api/idea-trading/guards");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement guards = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        (guards.GetProperty("openTrades").GetInt32(), guards.GetProperty("maxOpenTrades").GetInt32(), guards.GetProperty("openLongs").GetInt32(), guards.GetProperty("openShorts").GetInt32(), guards.GetProperty("maxSameDirection").GetInt32())
            .ShouldBe((3, 8, 2, 1, 5));
        (Number(guards.GetProperty("daySum")), Number(guards.GetProperty("dailyLossLimit"))).ShouldBe((-30.15m, 30m));
        (guards.GetProperty("triggeredAt").GetDateTimeOffset(), guards.GetProperty("resumesAt").GetDateTimeOffset()).ShouldBe((reached, new DateTimeOffset(2024, 3, 11, 0, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public async Task Guards_that_are_off_have_no_limit_and_a_day_without_trades_has_no_trigger()
    {
        JsonElement guards = JsonDocument.Parse(await _scenario.Client.GetStringAsync("/api/idea-trading/guards")).RootElement;

        (Number(guards.GetProperty("daySum")), guards.GetProperty("dailyLossLimit").ValueKind, guards.GetProperty("triggeredAt").ValueKind, guards.GetProperty("maxSameDirection").ValueKind)
            .ShouldBe((0m, JsonValueKind.Null, JsonValueKind.Null, JsonValueKind.Null));
    }

    // The journal of the autos: who took the trade, the auto, the paper trades in both groups, the totals of the paper ones.
    [Test]
    public async Task Journal_carries_who_took_each_trade_and_filters_by_it_and_by_the_auto()
    {
        Guid auto = await CreateAsync("Импульс лонг");
        IdeaTrade manual = await _scenario.CreateTradeAsync();
        IdeaTrade byAuto = await _scenario.CreateTradeAsync(autoId: auto);
        await InsertPaperTradeAsync(auto, "closed_at_target", Now.AddHours(-2), 1.5m);
        await InsertPaperTradeAsync(auto, "open", null, null);

        JsonElement all = await JournalAsync();
        JsonElement paper = await JournalAsync("origin=paper");
        JsonElement mine = await JournalAsync("origin=auto&autoId=" + auto);

        all.GetProperty("open").EnumerateArray().Select(item => item.GetProperty("origin").GetString()).Order().ShouldBe(["auto", "manual", "paper"]);
        all.GetProperty("closed").GetProperty("items").EnumerateArray().Single().GetProperty("origin").GetString().ShouldBe("paper");
        all.GetProperty("paperTotals").GetProperty("targets").GetInt32().ShouldBe(1);
        all.GetProperty("totals").GetProperty("targets").GetInt32().ShouldBe(0);
        paper.GetProperty("open").GetArrayLength().ShouldBe(1);
        mine.GetProperty("open").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(byAuto.Id);
        mine.GetProperty("open").EnumerateArray().Single().GetProperty("autoName").GetString().ShouldBe("Импульс лонг");
        manual.AutoId.ShouldBeNull();
        await ShouldBeRefusedOnAsync(await _scenario.Client.GetAsync("/api/idea-trades?origin=robot"), "origin");
        await ShouldBeRefusedOnAsync(await _scenario.Client.GetAsync($"/api/idea-trades?autoId={Guid.NewGuid()}"), "autoId");
    }

    private static decimal Number(JsonElement value)
    {
        return decimal.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private DateTimeOffset Now => _scenario.Factory.Time.GetUtcNow();

    private object Body(
        string name,
        Guid? accountId = null,
        decimal amount = 100m,
        string[]? setups = null,
        string[]? types = null,
        string[]? intervals = null,
        string? side = null,
        string? category = null,
        string? badge = null,
        decimal minRiskReward = 1.2m,
        int minCases = 0)
    {
        return new
        {
            name,
            accountId = accountId ?? _scenario.Account,
            amount,
            setups = setups ?? ["impulse", "breakout"],
            types = types ?? ["target_2r", "pullback"],
            intervals = intervals ?? ["15", "60"],
            side,
            category,
            badge,
            minRiskReward,
            minCases
        };
    }

    private async Task<Guid> CreateAsync(string name, decimal amount = 100m, string? side = null, string? category = null, string? badge = null, decimal minRiskReward = 1.2m, int minCases = 0)
    {
        HttpResponseMessage response = await _scenario.Client.PostAsJsonAsync(Url, Body(name, _scenario.Account, amount, side: side, category: category, badge: badge, minRiskReward: minRiskReward, minCases: minCases));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GetAsync(Guid id)
    {
        return JsonDocument.Parse(await _scenario.Client.GetStringAsync($"{Url}/{id}")).RootElement;
    }

    private async Task<List<JsonElement>> ListAsync()
    {
        return [.. JsonDocument.Parse(await _scenario.Client.GetStringAsync(Url)).RootElement.EnumerateArray()];
    }

    private async Task<JsonElement> RefusalsAsync(string query)
    {
        return JsonDocument.Parse(await _scenario.Client.GetStringAsync($"{Url}/refusals?{query}")).RootElement;
    }

    private async Task<JsonElement> JournalAsync(string? query = null)
    {
        return JsonDocument.Parse(await _scenario.Client.GetStringAsync("/api/idea-trades" + (query is null ? "" : "?" + query))).RootElement;
    }

    // The body is refused for the field; with an id it is a save of that auto.
    private async Task RefusedOnAsync(object body, string field, Guid? id = null)
    {
        HttpResponseMessage response = id is { } auto ? await _scenario.Client.PutAsJsonAsync($"{Url}/{auto}", body) : await _scenario.Client.PostAsJsonAsync(Url, body);

        await ShouldBeRefusedOnAsync(response, field);
    }

    private static async Task ShouldBeRefusedOnAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(error => error.Name).ShouldContain(field);
    }

    private async Task InsertPaperTradeAsync(Guid auto, string status, DateTimeOffset? closedAt, decimal? resultR)
    {
        // One unit of risk is 1 USDT: the entry of 100 for 1 coin with the stop at 99 pays R as the money of the exit.
        decimal exit = resultR is { } r ? 100m + r : 0m;
        bool target = status == "closed_at_target";
        bool stop = status == "closed_at_stop";
        await using var database = NpgsqlDataSource.Create(_scenario.Factory.ConnectionString);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO idea_paper_trades (id, idea_id, auto_id, account_id, category, symbol, setup, interval, type, side, entry_price, stop, target, quantity, amount, risk, status, status_at,
                entry_quantity, entry_value, entry_fee, take_profit_quantity, take_profit_value, take_profit_fee, stop_loss_quantity, stop_loss_value, stop_loss_fee, created_at, opened_at, stepped_until)
            VALUES (gen_random_uuid(), gen_random_uuid(), @auto, @account, 'linear', 'BTCUSDT', 'impulse', '15', 'target_2r', 'long', 100, 99, 102, 1, 100, 1, @status, @at,
                1, 100, 0, @tpq, @tpv, 0, @slq, @slv, 0, @created, @created, @at)
            """,
            new
            {
                auto,
                account = _scenario.Account,
                status,
                at = closedAt ?? Now.AddHours(-4),
                created = Now.AddHours(-4),
                tpq = target ? 1m : 0m,
                tpv = target ? exit : 0m,
                slq = stop ? 1m : 0m,
                slv = stop ? exit : 0m
            });
    }

    private async Task InsertRefusalAsync(Guid auto, DateTimeOffset at, string reason, string? detail = null)
    {
        await using var database = NpgsqlDataSource.Create(_scenario.Factory.ConnectionString);
        await using NpgsqlConnection connection = await database.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO idea_auto_refusals (id, idea_id, auto_id, mode, created_at, category, symbol, setup, interval, type, side, reasons, detail) VALUES (gen_random_uuid(), gen_random_uuid(), @auto, 'live', @at, 'linear', 'SUIUSDT', 'impulse', '15', 'target_2r', 'long', ARRAY[@reason], @detail::jsonb)",
            new { auto, at, reason, detail });
    }

    // A real trade that closed at the time with the result in money: it entered for 100 and left for 100 plus the result.
    private async Task ClosedTradeAsync(decimal result, DateTimeOffset at)
    {
        IdeaTrade trade = await _scenario.CreateTradeAsync();
        await _scenario.ChangeTradeAsync(
            trade.Id,
            locked => locked.EntryGrown(new LegTotals(1m, 100m, 0m), at.AddMinutes(-5)).ExitGrown(IdeaTradeLeg.StopLoss, new LegTotals(1m, 100m + result, 0m), at));
    }
}