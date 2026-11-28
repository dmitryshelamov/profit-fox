using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Dapper;

using Npgsql;

using ProfitFox.Presentation.Contracts.Errors;
using ProfitFox.Presentation.Contracts.Trades;
using ProfitFox.Tests.Integration.Application.Commands.Trades;
using ProfitFox.Tests.Integration.MarketData;

namespace ProfitFox.Tests.Integration.Presentation.Controllers;

/// <summary>
///     Change take-idea-trades, task 8.1 (design.md, decision 13; spec trade-risk): <c>/api/risk</c> — the settings with 422
///     at the field, the state of the risk module with the accounts on the poll, and the lift of the ban with 409.
/// </summary>
[NonParallelizable]
public sealed class RiskControllerTests
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private MarketDataServer _market = null!;
    private TradeBench _bench = null!;
    private BenchTestApplication _api = null!;

    [SetUp]
    public async Task Start()
    {
        _bench = await TradeBench.Start(limits: false);
        _market = await MarketDataServer.Start();
        _api = new BenchTestApplication(_market, bybit: _bench.Bybit);
    }

    [TearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
        await _market.DisposeAsync();
        await _bench.DisposeAsync();
    }

    // Spec «Значения по умолчанию», «Пределы не заданы».
    [Test]
    public async Task A_fresh_bench_has_no_limits_and_the_default_times_and_attempts()
    {
        TradeSettingsResponse settings = (await _api.Http.GetFromJsonAsync<TradeSettingsResponse>("/api/risk/settings", Json))!;
        RiskStateResponse state = (await _api.Http.GetFromJsonAsync<RiskStateResponse>("/api/risk/state", Json))!;

        Assert.That((settings.Leverage, settings.ExpiryMarginMinutes, settings.EntryWaitSeconds, settings.StopAttempts, settings.SavedAt), Is.EqualTo(((int?)null, 1, 30, 3, (long?)null)));
        Assert.That((state.LimitsSet, state.Missing.Count, state.TraderAlive, state.Accounts), Is.EqualTo((false, 6, true, 1)));
    }

    // Spec «Настройки сохранены».
    [Test]
    public async Task Saved_settings_are_given_back()
    {
        var request = new TradeSettingsRequest(_bench.Main.Id, 100m, 3, 200m, 5, 1000m, 3m, 50m, 5, 45, 4);

        HttpResponseMessage saved = await _api.Http.PutAsJsonAsync("/api/risk/settings", request);

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        TradeSettingsResponse read = (await _api.Http.GetFromJsonAsync<TradeSettingsResponse>("/api/risk/settings", Json))!;
        Assert.That(read with { SavedAt = null }, Is.EqualTo(new TradeSettingsResponse(_bench.Main.Id, 100m, 3, 200m, 5, 1000m, 3m, 50m, 5, 45, 4, null)));
        Assert.That(read.SavedAt, Is.EqualTo((await saved.Content.ReadFromJsonAsync<TradeSettingsResponse>(Json))!.SavedAt));
    }

    // Spec «Отказ под полем».
    [Test]
    public async Task A_loss_too_wide_for_the_leverage_is_422_at_the_loss()
    {
        HttpResponseMessage refused = await _api.Http.PutAsJsonAsync("/api/risk/settings", new TradeSettingsRequest(null, null, 10, null, null, null, 6m, null, null, null, null));

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
        Assert.That(
            (await refused.Content.ReadFromJsonAsync<ErrorResponse>(Json))!.Errors.Select(error => (error.Field, error.Reason)),
            Is.EqualTo(new[] { ((string?)"maxLossPercent", "при плече 10× предел потери должен быть меньше 5 %") }));
    }

    // Spec «Состояние риска», «Аккаунт на опросе».
    [Test]
    public async Task The_state_names_the_loss_the_counters_against_the_limits_and_the_accounts_on_the_poll()
    {
        await _api.Http.PutAsJsonAsync("/api/risk/settings", new TradeSettingsRequest(null, 100m, 3, 200m, 5, 1000m, 3m, 50m, null, null, null));
        await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest((await _bench.Idea()).Id, null, null));
        await using (NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("UPDATE trades SET unrealised = -4.20");
            await connection.ExecuteAsync("UPDATE exchange_accounts SET feed = 'poll', feed_changed_at = '2026-11-28T15:42:00Z'");
        }

        RiskStateResponse state = (await _api.Http.GetFromJsonAsync<RiskStateResponse>("/api/risk/state", Json))!;

        Assert.That((state.LimitsSet, state.DayLoss, state.DayLimit, state.OpenTrades, state.MaxOpenTrades, state.OpenAmount, state.MaxTotalAmount), Is.EqualTo((true, 4.20m, (decimal?)50m, 1, (int?)5, 100m, (decimal?)1000m)));
        Assert.That(state.Polled.Single(), Is.EqualTo(new PolledAccountResponse(_bench.Main.Id, "Основной", new DateTimeOffset(2026, 11, 28, 15, 42, 0, TimeSpan.Zero).ToUnixTimeMilliseconds())));
        Assert.That(state.Halt.HaltedAt, Is.Null);
    }

    // Spec «Запрет снят», «Ещё закрываются».
    [Test]
    public async Task The_ban_is_lifted_409_while_a_trade_closes_and_200_after()
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO trade_halt (id, halted_at, loss, limit_value) VALUES (1, now(), 52.10, 50)");
        await _api.Http.PutAsJsonAsync("/api/risk/settings", new TradeSettingsRequest(null, 100m, 3, 200m, 5, 1000m, 3m, 50m, null, null, null));
        await connection.ExecuteAsync("UPDATE trade_halt SET halted_at = NULL WHERE id = 1");
        await _api.Http.PostAsJsonAsync("/api/trades", new EntryRequest((await _bench.Idea()).Id, null, null));
        await connection.ExecuteAsync("UPDATE trade_halt SET halted_at = now() WHERE id = 1; UPDATE trades SET state = 'closing'");

        HttpResponseMessage closing = await _api.Http.PostAsync("/api/risk/halt/lift", null);
        await connection.ExecuteAsync("UPDATE trades SET state = 'closed'");
        HttpResponseMessage lifted = await _api.Http.PostAsync("/api/risk/halt/lift", null);

        Assert.That((closing.StatusCode, lifted.StatusCode), Is.EqualTo((HttpStatusCode.Conflict, HttpStatusCode.OK)));
        HaltResponse halt = (await lifted.Content.ReadFromJsonAsync<HaltResponse>(Json))!;
        Assert.That((halt.HaltedAt, halt.LiftedAt is not null), Is.EqualTo(((long?)null, true)));
    }
}
