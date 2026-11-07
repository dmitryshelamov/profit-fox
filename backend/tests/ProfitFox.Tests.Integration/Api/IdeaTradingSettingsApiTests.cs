using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ProfitFox.Tests.Integration.Workers;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>Requirement "Настройки торговли идеями".</summary>
public class IdeaTradingSettingsApiTests
{
    private const string Url = "/api/idea-trading/settings";

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

    // Scenario "Настройки по умолчанию".
    [Test]
    public async Task Settings_never_saved_are_the_defaults_without_an_account()
    {
        JsonElement settings = await ReadAsync();

        settings.GetProperty("amount").GetString().ShouldBe("20");
        settings.GetProperty("maxAmount").GetString().ShouldBe("200");
        settings.GetProperty("maxOpenTrades").GetInt32().ShouldBe(5);
        settings.GetProperty("maxTotalRisk").GetString().ShouldBe("5");
        settings.GetProperty("minRiskReward").GetString().ShouldBe("1.5");
        settings.GetProperty("accountId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Saved_settings_are_read_back()
    {
        HttpResponseMessage saved = await _scenario.Client.PutAsJsonAsync(Url, Body(accountId: _scenario.Account, amount: 25.5m, maxOpenTrades: 3));

        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement settings = await ReadAsync();
        settings.GetProperty("accountId").GetGuid().ShouldBe(_scenario.Account);
        settings.GetProperty("amount").GetString().ShouldBe("25.5");
        settings.GetProperty("maxOpenTrades").GetInt32().ShouldBe(3);
    }

    // Scenario "Сумма ноль".
    [Test]
    public async Task Amount_of_zero_is_refused_by_its_field_and_nothing_changes()
    {
        HttpResponseMessage response = await _scenario.Client.PutAsJsonAsync(Url, Body(amount: 0m));

        await ShouldBeRefusedOnAsync(response, "amount");
        (await ReadAsync()).GetProperty("amount").GetString().ShouldBe("20");
    }

    // Scenario "Риск ноль".
    [Test]
    public async Task Total_risk_of_zero_is_refused_by_its_field_and_nothing_changes()
    {
        HttpResponseMessage response = await _scenario.Client.PutAsJsonAsync(Url, Body(maxTotalRisk: 0m));

        await ShouldBeRefusedOnAsync(response, "maxTotalRisk");
        (await ReadAsync()).GetProperty("maxTotalRisk").GetString().ShouldBe("5");
    }

    [Test]
    public async Task Every_value_out_of_range_is_refused_by_its_own_field()
    {
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(maxAmount: 0m)), "maxAmount");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(maxOpenTrades: 0)), "maxOpenTrades");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(maxTotalRisk: -1m)), "maxTotalRisk");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(minRiskReward: -0.1m)), "minRiskReward");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(accountId: Guid.NewGuid())), "accountId");
    }

    [Test]
    public async Task Minimum_risk_reward_of_zero_is_allowed()
    {
        (await _scenario.Client.PutAsJsonAsync(Url, Body(minRiskReward: 0m))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static object Body(
        Guid? accountId = null,
        decimal amount = 20m,
        decimal maxAmount = 200m,
        int maxOpenTrades = 5,
        decimal maxTotalRisk = 5m,
        decimal minRiskReward = 1.5m,
        decimal? dailyLossLimit = null,
        int? maxSameDirection = null,
        decimal? autosBudget = null)
    {
        return new { accountId, amount, maxAmount, maxOpenTrades, maxTotalRisk, minRiskReward, dailyLossLimit, maxSameDirection, autosBudget };
    }

    private async Task<JsonElement> ReadAsync()
    {
        HttpResponseMessage response = await _scenario.Client.GetAsync(Url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    // Scenario "Защиты по умолчанию": both off, and none of the other settings changes.
    [Test]
    public async Task Guards_of_the_autos_are_off_until_they_are_saved()
    {
        JsonElement settings = await ReadAsync();

        settings.GetProperty("dailyLossLimit").ValueKind.ShouldBe(JsonValueKind.Null);
        settings.GetProperty("maxSameDirection").ValueKind.ShouldBe(JsonValueKind.Null);
        settings.GetProperty("autosBudget").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // The budget of all the autos is saved, read back and switched off by an empty value.
    [Test]
    public async Task Budget_of_the_autos_is_saved_and_an_empty_value_switches_it_off()
    {
        (await _scenario.Client.PutAsJsonAsync(Url, Body(autosBudget: 200m))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync()).GetProperty("autosBudget").GetString().ShouldBe("200");

        (await _scenario.Client.PutAsJsonAsync(Url, Body())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReadAsync()).GetProperty("autosBudget").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // Scenarios "Выключить защиту" and the save: an empty value switches the guard off.
    [Test]
    public async Task Guards_are_saved_and_an_empty_value_switches_one_off()
    {
        (await _scenario.Client.PutAsJsonAsync(Url, Body(dailyLossLimit: 30m, maxSameDirection: 5))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        JsonElement saved = await ReadAsync();
        (saved.GetProperty("dailyLossLimit").GetString(), saved.GetProperty("maxSameDirection").GetInt32()).ShouldBe(("30", 5));

        (await _scenario.Client.PutAsJsonAsync(Url, Body(dailyLossLimit: 30m))).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        JsonElement off = await ReadAsync();
        (off.GetProperty("dailyLossLimit").GetString(), off.GetProperty("maxSameDirection").ValueKind).ShouldBe(("30", JsonValueKind.Null));
    }

    // Scenario "Дневной стоп ноль".
    [Test]
    public async Task Guard_out_of_range_is_refused_by_its_own_field_and_nothing_changes()
    {
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(dailyLossLimit: 0m)), "dailyLossLimit");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(dailyLossLimit: -5m)), "dailyLossLimit");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(maxSameDirection: 0)), "maxSameDirection");
        await ShouldBeRefusedOnAsync(await _scenario.Client.PutAsJsonAsync(Url, Body(autosBudget: 0m)), "autosBudget");
        (await ReadAsync()).GetProperty("dailyLossLimit").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    private static async Task ShouldBeRefusedOnAsync(HttpResponseMessage response, string field)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonElement errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("errors");
        errors.EnumerateObject().Select(error => error.Name).ShouldBe([field]);
    }
}