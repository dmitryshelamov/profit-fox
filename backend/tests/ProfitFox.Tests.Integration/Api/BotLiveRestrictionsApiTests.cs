using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>A bot that trades, or holds an open position, keeps the settings it trades with.</summary>
public class BotLiveRestrictionsApiTests
{
    private const string Bots = "/api/bots";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2024-03-10T12:00:00Z");

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _ema;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0 };
        _factory.Bybit.Instruments.UnionWith([("spot", "BTCUSDT"), ("spot", "ETHUSDT")]);
        _client = _factory.CreateClient();
        HttpResponseMessage strategy = await _client.PostAsJsonAsync("/api/strategies", new { name = "EMA", definition = StrategyFixtures.Element("ema") });
        _ema = (await strategy.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // Scenario "Новый бот".
    [Test]
    public async Task New_bot_reads_as_stopped()
    {
        Guid id = await CreateBotAsync();

        JsonElement read = await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}");
        JsonElement listed = (await _client.GetFromJsonAsync<JsonElement>(Bots)).EnumerateArray().Single();
        foreach (JsonElement bot in new[] { read, listed })
        {
            bot.GetProperty("status").GetString().ShouldBe("stopped");
            bot.GetProperty("statusReason").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task Failed_bot_shows_its_reason()
    {
        Guid id = await CreateBotAsync();
        await SetLiveAsync(id, bot => bot.Start(1, new Dictionary<string, ProfitFox.Domain.ValueObjects.Strategies.NodeValue>()).Fail("Insufficient funds.").Stopped());

        JsonElement read = await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}");

        (read.GetProperty("status").GetString(), read.GetProperty("statusReason").GetString()).ShouldBe(("failed", "Insufficient funds."));
    }

    // Scenario "Правка запущенного бота".
    [TestCase(LiveBotStatus.Running)]
    [TestCase(LiveBotStatus.Paused)]
    [TestCase(LiveBotStatus.Stopping)]
    public async Task Working_bot_is_not_changed(LiveBotStatus status)
    {
        Guid id = await CreateBotAsync();
        await SetLiveAsync(id, bot => bot with { Status = status });
        JsonObject body = Body();
        body["balance"] = 1500;

        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Bots}/{id}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("balance").GetString().ShouldBe("1000");
    }

    // A save of the bot that changes none of its settings, such as the same form sent again, is no change.
    [Test]
    public async Task Working_bot_takes_the_same_settings()
    {
        Guid id = await CreateBotAsync();
        await SetLiveAsync(id, bot => bot with { Status = LiveBotStatus.Running });

        (await _client.PutAsJsonAsync($"{Bots}/{id}", Body())).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    // Scenario "Смена инструмента с позицией".
    [Test]
    public async Task Instrument_of_a_bot_with_an_open_position_stays()
    {
        Guid id = await CreateBotAsync();
        await OpenPositionAsync(id);
        JsonObject body = Body();
        body["symbol"] = "ETHUSDT";

        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Bots}/{id}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("symbol", out _).ShouldBeTrue();
    }

    [Test]
    public async Task Interval_of_a_bot_with_an_open_position_stays()
    {
        Guid id = await CreateBotAsync();
        await OpenPositionAsync(id);
        JsonObject body = Body();
        body["interval"] = "240";

        HttpResponseMessage response = await _client.PutAsJsonAsync($"{Bots}/{id}", body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("interval", out _).ShouldBeTrue();
    }

    // Scenario "Смена баланса с позицией".
    [Test]
    public async Task Balance_of_a_stopped_bot_with_an_open_position_changes()
    {
        Guid id = await CreateBotAsync();
        await OpenPositionAsync(id);
        JsonObject body = Body();
        body["balance"] = 1500;

        (await _client.PutAsJsonAsync($"{Bots}/{id}", body)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{id}")).GetProperty("balance").GetString().ShouldBe("1500");
    }

    // Scenario "Запущенный бот" of the deletion.
    [Test]
    public async Task Working_bot_is_not_deleted()
    {
        Guid id = await CreateBotAsync();
        await SetLiveAsync(id, bot => bot with { Status = LiveBotStatus.Running });

        (await _client.DeleteAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await _client.GetAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Scenario "Остановленный бот с позицией".
    [Test]
    public async Task Bot_with_an_open_position_is_not_deleted()
    {
        Guid id = await CreateBotAsync();
        await OpenPositionAsync(id);

        HttpResponseMessage response = await _client.DeleteAsync($"{Bots}/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("open position");
    }

    [Test]
    public async Task Stopped_bot_without_a_position_is_deleted_with_its_books()
    {
        Guid id = await CreateBotAsync();
        await OpenPositionAsync(id, open: false);

        (await _client.DeleteAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await _client.GetAsync($"{Bots}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private Task OpenPositionAsync(Guid id, bool open = true)
    {
        return InUnitAsync(async (repository, cancellationToken) =>
        {
            await repository.LockAsync(id, Now, cancellationToken);
            await repository.SavePositionAsync(new LivePosition(id, 1, open, false, open ? 0.5m : 0.0001m, 50m, 50m, 0m, 0m, 100m, Now, 0, null), cancellationToken);
        });
    }

    private Task SetLiveAsync(Guid id, Func<LiveBot, LiveBot> change)
    {
        return InUnitAsync(async (repository, cancellationToken) =>
        {
            LiveBot bot = (await repository.LockAsync(id, Now, cancellationToken))!;
            await repository.SaveAsync(change(bot), Now, cancellationToken);
        });
    }

    private async Task InUnitAsync(Func<ILiveBotRepository, CancellationToken, Task> work)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ILiveBotRepository repository = scope.ServiceProvider.GetRequiredService<ILiveBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(
            async cancellationToken =>
            {
                await work(repository, cancellationToken);

                return 0;
            },
            CancellationToken.None);
    }

    private async Task<Guid> CreateBotAsync()
    {
        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, Body());
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private JsonObject Body()
    {
        return new JsonObject
        {
            ["name"] = "BTC EMA час",
            ["strategyId"] = _ema,
            ["category"] = "spot",
            ["symbol"] = "BTCUSDT",
            ["interval"] = "60",
            ["balance"] = 1000
        };
    }
}