using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Transactions;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>The owner's actions with a bot in live trading: they change its status, a Worker does the trading.</summary>
public class LiveBotCommandsApiTests
{
    private const string Bots = "/api/bots";

    private ProfitFoxFactory _factory = null!;
    private HttpClient _client = null!;
    private Guid _ema;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        _factory.Bybit.Instruments.UnionWith([("spot", "BTCUSDT"), ("linear", "BTCUSDT")]);
        _factory.Bybit.Coins[("spot", "BTCUSDT")] = ("BTC", "USDT");
        _factory.Bybit.Coins[("linear", "BTCUSDT")] = ("BTC", "USDT");
        _factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "USDT", Arg.Any<CancellationToken>()).Returns(5000m);
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

    // Scenario "Запуск остановленного бота".
    [Test]
    public async Task Started_bot_runs_on_the_latest_version_of_its_strategy()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));

        (await StartAsync(bot)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await StatusAsync(bot)).ShouldBe("running");
        LiveBot live = await LiveAsync(bot);
        (live.StrategyVersion, live.StartPending).ShouldBe((1, true));
        live.ParameterValues.Keys.ShouldBe(["fast", "slow", "amount"], ignoreOrder: true);
    }

    // Scenario "Правка стратегии запущенного бота".
    [Test]
    public async Task Running_bot_keeps_the_version_it_started_with()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        await StartAsync(bot);

        (await _client.PutAsJsonAsync($"/api/strategies/{_ema}", new { name = "EMA 2", definition = StrategyFixtures.Element("ema") })).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        (await LiveAsync(bot)).StrategyVersion.ShouldBe(1);
    }

    // Scenario "Повторный запуск".
    [Test]
    public async Task Start_of_a_running_bot_is_a_conflict()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        await StartAsync(bot);

        (await StartAsync(bot)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await StatusAsync(bot)).ShouldBe("running");
    }

    // Scenario "Бот без аккаунта".
    [Test]
    public async Task Bot_without_an_account_does_not_start()
    {
        Guid bot = await CreateBotAsync(null);

        await ShouldBeRefusedAsync(await StartAsync(bot), "no exchange account");
        (await StatusAsync(bot)).ShouldBe("stopped");
    }

    [Test]
    public async Task Bot_whose_key_bybit_no_longer_takes_does_not_start()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeKeyRejectedException("Bybit does not accept the key: API key is invalid."));

        await ShouldBeRefusedAsync(await StartAsync(bot), "Bybit does not accept the key");
    }

    [Test]
    public async Task Bot_whose_key_no_longer_trades_its_category_does_not_start()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeKeyInfo(100, false, true, false, new HashSet<MarketCategory> { MarketCategory.Linear }));

        await ShouldBeRefusedAsync(await StartAsync(bot), "does not trade");
    }

    [Test]
    public async Task Linear_bot_of_an_account_in_hedge_mode_does_not_start()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Linear), "linear");
        _factory.Ledger.IsHedgeModeAsync(Arg.Any<ExchangeKey>(), new Instrument(MarketCategory.Linear, "BTCUSDT"), Arg.Any<CancellationToken>()).Returns(true);

        await ShouldBeRefusedAsync(await StartAsync(bot), "hedge mode");
    }

    [Test]
    public async Task Start_when_bybit_does_not_answer_is_unavailable()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        _factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ExchangeUnavailableException("Bybit did not answer."));

        (await StartAsync(bot)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await StatusAsync(bot)).ShouldBe("stopped");
    }

    // Scenario "Денег не хватает с учётом другого бота".
    [Test]
    public async Task Other_working_bots_of_the_account_take_their_balance()
    {
        Guid account = await CreateAccountAsync(MarketCategory.Spot);
        _factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), "USDT", Arg.Any<CancellationToken>()).Returns(1500m);
        Guid first = await CreateBotAsync(account);
        Guid second = await CreateBotAsync(account);
        (await StartAsync(first)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ShouldBeRefusedAsync(await StartAsync(second), "Not enough free money");
        (await StatusAsync(second)).ShouldBe("stopped");
    }

    [Test]
    public async Task Pause_resume_and_stop_change_the_status()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        await StartAsync(bot);

        (await PostAsync(bot, "pause")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(bot)).ShouldBe("paused");
        (await PostAsync(bot, "pause")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(bot, "resume")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(bot)).ShouldBe("running");
        (await PostAsync(bot, "resume")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(bot, "stop")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await StatusAsync(bot)).ShouldBe("stopping");
        (await PostAsync(bot, "stop")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // Scenarios "Закрытие лонга" and "Запущенный бот" of the close.
    [Test]
    public async Task Position_of_a_stopped_bot_is_closed_on_request()
    {
        Guid bot = await CreateBotAsync(await CreateAccountAsync(MarketCategory.Spot));
        (await PostAsync(bot, "close-position")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await InUnitAsync(async (repository, cancellationToken) =>
        {
            await repository.LockAsync(bot, _factory.Time.GetUtcNow(), cancellationToken);
            await repository.SavePositionAsync(new LivePosition(bot, 1, true, false, 0.5m, 50m, 50m, 0m, 0m, 100m, _factory.Time.GetUtcNow(), 0, null), cancellationToken);
        });

        (await PostAsync(bot, "close-position")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await LiveAsync(bot)).CloseRequested.ShouldBeTrue();
        (await StatusAsync(bot)).ShouldBe("stopped");
        (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{bot}")).GetProperty("closeRequested").GetBoolean().ShouldBeTrue();
        (await StartAsync(bot)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [TestCase("start")]
    [TestCase("pause")]
    [TestCase("resume")]
    [TestCase("stop")]
    [TestCase("close-position")]
    public async Task Action_with_an_unknown_bot_is_not_found(string action)
    {
        (await PostAsync(Guid.NewGuid(), action)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, string reason)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!.ShouldContain(reason);
    }

    private Task<HttpResponseMessage> StartAsync(Guid bot)
    {
        return PostAsync(bot, "start");
    }

    private Task<HttpResponseMessage> PostAsync(Guid bot, string action)
    {
        return _client.PostAsync($"{Bots}/{bot}/{action}", null);
    }

    private async Task<string?> StatusAsync(Guid bot)
    {
        return (await _client.GetFromJsonAsync<JsonElement>($"{Bots}/{bot}")).GetProperty("status").GetString();
    }

    private async Task<LiveBot> LiveAsync(Guid bot)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetAsync(bot, CancellationToken.None);
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

    private async Task<Guid> CreateAccountAsync(MarketCategory category)
    {
        _factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeKeyInfo(Random.Shared.NextInt64(1, long.MaxValue), false, true, false, new HashSet<MarketCategory> { category }));
        HttpResponseMessage created = await _client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Основной", apiKey = "api-key", apiSecret = "api-secret" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBotAsync(Guid? account, string category = "spot")
    {
        var body = new JsonObject
        {
            ["name"] = "BTC EMA час",
            ["strategyId"] = _ema,
            ["category"] = category,
            ["symbol"] = "BTCUSDT",
            ["interval"] = "60",
            ["balance"] = 1000,
            ["exchangeAccountId"] = account
        };
        HttpResponseMessage response = await _client.PostAsJsonAsync(Bots, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}