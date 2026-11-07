using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Npgsql;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Integration.Workers;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirements "Расчёт входа" and "Когда вход отклоняется": the entry of an idea is calculated from the current price and the account, nothing is placed.</summary>
public class IdeaEntryApiTests
{
    private LiveTradingScenario _scenario = null!;
    private NpgsqlDataSource _database = null!;

    [SetUp]
    public async Task SetUp()
    {
        _scenario = await LiveTradingScenario.CreateAsync();
        _database = NpgsqlDataSource.Create(_scenario.Factory.ConnectionString);
        _scenario.Factory.Bybit.Rules[("linear", "BTCUSDT")] = ("0.01", "0.01", "0.01", "1");
    }

    [TearDown]
    public async Task TearDown()
    {
        await _database.DisposeAsync();
        await _scenario.DisposeAsync();
    }

    // Scenario "Объём по сумме": 47.3 / 100 = 0.473 rounds down to 0.47; with the slippage of linear the loss on 1 USDT is 0.0220790055,
    // the profit 0.037858011.
    [Test]
    public async Task Volume_comes_from_the_amount_and_the_current_price()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");

        JsonElement entry = await EntryAsync(idea, amount: "47.3");

        entry.GetProperty("entryPrice").GetString().ShouldBe("100");
        entry.GetProperty("quantity").GetString().ShouldBe("0.47");
        entry.GetProperty("amount").GetString().ShouldBe("47.00");
        decimal.Parse(entry.GetProperty("risk").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(1.037713m, 0.000001m);
        Math.Round(decimal.Parse(entry.GetProperty("riskReward").GetString()!, System.Globalization.CultureInfo.InvariantCulture), 2).ShouldBe(1.71m);
        entry.GetProperty("riskRewardWithoutFees").GetString().ShouldBe("2");
        entry.GetProperty("freeMoney").GetString().ShouldBe("10000");
        decimal.Parse(entry.GetProperty("profit").GetString()!, System.Globalization.CultureInfo.InvariantCulture).ShouldBe(1.779327m, 0.000001m);
        entry.GetProperty("reasons").GetArrayLength().ShouldBe(0);
        _scenario.Exchange.Sent.ShouldBeEmpty();
    }

    // Scenario "Сумма упёрлась в предел": the amount is not cut, it is named as the reason.
    [Test]
    public async Task Amount_above_the_limit_is_a_reason_and_is_not_cut()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");

        JsonElement entry = await EntryAsync(idea, amount: "250");

        entry.GetProperty("quantity").GetString().ShouldBe("2.50");
        Reasons(entry).ShouldContain("amount_above_max");
    }

    // The pullback enters at the price of its idea, whatever the market does.
    [Test]
    public async Task Pullback_enters_at_the_price_of_its_idea()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m, IdeaType.Pullback);
        SetPrice("101.5");

        (await EntryAsync(idea, amount: "47.3")).GetProperty("entryPrice").GetString().ShouldBe("100");
    }

    // The limit of a pullback is at its own price, but the market may be through the stop already.
    [Test]
    public async Task Pullback_with_the_market_beyond_the_stop_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m, IdeaType.Pullback);
        SetPrice("97.5");

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldContain("price_beyond_stop");
    }

    // Scenario "Цена ушла за цель".
    [Test]
    public async Task Price_beyond_the_target_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(23.48m, 23.01m, 24.42m);
        SetPrice("24.50");

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldContain("price_beyond_target");
    }

    // Scenario "R:R упал".
    [Test]
    public async Task Risk_reward_below_the_lowest_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(23.48m, 23.01m, 24.42m);
        SetPrice("23.90");

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["risk_reward_too_low"]);
    }

    [Test]
    public async Task Idea_that_is_over_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m, status: IdeaStatus.Expired);
        SetPrice("100");

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["idea_not_open"]);
    }

    // Scenario "Противоположный бот".
    [Test]
    public async Task Running_bot_of_the_opposite_direction_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        Guid bot = await _scenario.CreateBotAsync("ema-short", category: "linear");
        await _scenario.ActAsync(bot, "start");

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["opposite_side_open"]);
    }

    // Scenario "Та же сторона": a bot of the same direction is no reason.
    [Test]
    public async Task Running_bot_of_the_same_direction_is_no_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        Guid bot = await _scenario.CreateBotAsync("ema", category: "linear");
        await _scenario.ActAsync(bot, "start");

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBeEmpty();
    }

    // Scenario "Общий риск": 3 × 1.5 + 0.99 > 5.
    [Test]
    public async Task Total_risk_above_the_limit_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        for (var i = 0; i < 3; i++)
        {
            await _scenario.CreateTradeAsync(risk: 1.5m);
        }

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["total_risk_exceeded"]);
    }

    [Test]
    public async Task Open_trades_at_the_limit_are_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = _scenario.Account, amount = 47.3m, maxAmount = 200m, maxOpenTrades = 1, maxTotalRisk = 5m, minRiskReward = 1.5m });
        await _scenario.CreateTradeAsync(risk: 0.1m);

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["too_many_open_trades"]);
    }

    // Requirement "Когда вход отклоняется": what the key and the account allow.
    [Test]
    public async Task Hedge_mode_of_the_account_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        _scenario.Factory.Ledger.IsHedgeModeAsync(Arg.Any<ExchangeKey>(), Arg.Any<Instrument>(), Arg.Any<CancellationToken>()).Returns(true);

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["hedge_mode"]);
    }

    [Test]
    public async Task Market_the_key_does_not_trade_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        _scenario.Factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeKeyInfo(100, false, true, false, new HashSet<MarketCategory> { MarketCategory.Spot }));

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["category_not_traded"]);
    }

    [Test]
    public async Task Key_that_bybit_no_longer_accepts_is_the_missing_key()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        _scenario.Factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeKeyRejectedException("API key is invalid."));

        Reasons(await EntryAsync(idea, amount: "47.3")).ShouldBe(["no_key"]);
    }

    [Test]
    public async Task Free_money_below_the_amount_is_a_reason()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        _scenario.Factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(10m);

        JsonElement entry = await EntryAsync(idea, amount: "47.3");

        Reasons(entry).ShouldBe(["not_enough_money"]);
        entry.GetProperty("freeMoney").GetString().ShouldBe("10");
    }

    [Test]
    public async Task Entry_without_an_account_names_the_missing_key()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");

        HttpResponseMessage response = await _scenario.Client.GetAsync($"/api/ideas/{idea}/entry?amount=47.3");

        Reasons(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement).ShouldBe(["no_key"]);
    }

    [Test]
    public async Task Account_and_amount_come_from_the_settings_when_not_asked()
    {
        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        SetPrice("100");
        await _scenario.Client.PutAsJsonAsync("/api/idea-trading/settings", new { accountId = _scenario.Account, amount = 94.5m, maxAmount = 200m, maxOpenTrades = 5, maxTotalRisk = 5m, minRiskReward = 1.5m });

        HttpResponseMessage response = await _scenario.Client.GetAsync($"/api/ideas/{idea}/entry");

        JsonElement entry = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        entry.GetProperty("quantity").GetString().ShouldBe("0.94");
    }

    [Test]
    public async Task Unknown_idea_is_not_found_and_an_amount_of_zero_is_refused()
    {
        (await _scenario.Client.GetAsync($"/api/ideas/{Guid.NewGuid()}/entry?amount=47.3")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        Guid idea = await SaveIdeaAsync(100m, 98m, 104m);
        HttpResponseMessage response = await _scenario.Client.GetAsync($"/api/ideas/{idea}/entry?amount=0");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["amount"]);
    }

    private static IReadOnlyList<string> Reasons(JsonElement entry)
    {
        return [.. entry.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()!)];
    }

    private void SetPrice(string price)
    {
        _scenario.Factory.Bybit.Tickers[("linear", "BTCUSDT")] = (price, price, price, "1", "0.0001");
    }

    private async Task<Guid> SaveIdeaAsync(decimal entry, decimal stop, decimal target, IdeaType type = IdeaType.TwoR, IdeaStatus status = IdeaStatus.AwaitingEntry)
    {
        Idea idea = IdeaGenerator.Generate(_scenario.Factory.Time.GetUtcNow().AddMinutes(-5), status: status, instrument: LiveTradingScenario.Linear, type: type);
        idea = idea with { Track = idea.Track with { Plan = new IdeaPlan(type, IdeaSide.Long, entry, stop, target) } };
        await new IdeaRepository(_database).SaveAsync([idea], [], CancellationToken.None);

        return idea.Id;
    }

    private async Task<JsonElement> EntryAsync(Guid idea, string amount)
    {
        HttpResponseMessage response = await _scenario.Client.GetAsync($"/api/ideas/{idea}/entry?accountId={_scenario.Account}&amount={amount}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}