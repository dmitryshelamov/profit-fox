using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>
///     A trading Worker on the API's database with one exchange account on <see cref="FakeExchange" />: the history of BTCUSDT on
///     the spot closes at 100 every hour up to 11:00, and the clock of the app stands at 12:30.
/// </summary>
public sealed class LiveTradingScenario : IAsyncDisposable
{
    public static readonly DateTimeOffset LastOpen = DateTimeOffset.Parse("2024-03-10T11:00:00Z");

    private readonly List<WorkerHost> _workers = [];

    private LiveTradingScenario(ProfitFoxFactory factory, HttpClient client)
    {
        Factory = factory;
        Client = client;
    }

    public ProfitFoxFactory Factory { get; }

    public HttpClient Client { get; }

    public FakeExchange Exchange { get; } = new FakeExchange();

    public Guid Account { get; private set; }

    public IReadOnlyList<WorkerHost> Workers => _workers;

    public static async Task<LiveTradingScenario> CreateAsync()
    {
        var factory = new ProfitFoxFactory(await IntegrationEnvironment.CreateDatabaseAsync()) { Workers = 0 };
        foreach (string category in new[] { "spot", "linear" })
        {
            factory.Bybit.Instruments.Add((category, "BTCUSDT"));
            factory.Bybit.Coins[(category, "BTCUSDT")] = ("BTC", "USDT");
            factory.Bybit.Rules[(category, "BTCUSDT")] = ("0.01", "0.000001", "0.000001", "1");
        }


        for (DateTimeOffset open = LastOpen.AddHours(-23); open <= LastOpen; open = open.AddHours(1))
        {
            factory.Bybit.Klines.Add([open.ToString("O"), "100", "100", "100", "100", "1", "100"]);
        }

        factory.Ledger.GetFreeAsync(Arg.Any<ExchangeKey>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(10_000m);
        var scenario = new LiveTradingScenario(factory, factory.CreateClient());
        factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeKeyInfo(100, false, true, false, new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear }));
        HttpResponseMessage account = await scenario.Client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Основной", apiKey = "api-key", apiSecret = "api-secret" });
        account.StatusCode.ShouldBe(HttpStatusCode.Created);
        scenario.Account = (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        return scenario;
    }

    /// <summary>A trading Worker whose exchange is <see cref="Exchange" />.</summary>
    public WorkerHost StartWorker(Serilog.Core.ILogEventSink? logSink = null)
    {
        WorkerHost worker = Factory.StartWorker(
            services =>
            {
                services.RemoveAll<IExchangeLedger>();
                services.RemoveAll<IOrderGateway>();
                services.RemoveAll<IExchangeStreams>();
                services.AddSingleton<IExchangeLedger>(Exchange);
                services.AddSingleton<IOrderGateway>(Exchange);
                services.AddSingleton<IExchangeStreams>(Exchange);
            },
            workerSettings: new Dictionary<string, string?>
            {
                ["Trading:Enabled"] = "true",
                ["ExchangeAccounts:MasterKey"] = ProfitFoxFactory.TestMasterKey
            },
            logSink: logSink);
        _workers.Add(worker);

        return worker;
    }

    public async Task StopWorkerAsync(WorkerHost worker)
    {
        await worker.DisposeAsync();
        _workers.Remove(worker);
    }

    /// <summary>A bot on a reference strategy on BTCUSDT on the spot, hourly, with the account of the scenario.</summary>
    public Task<Guid> CreateBotAsync(string strategy = "dca", decimal balance = 1000m, string category = "spot", string symbol = "BTCUSDT")
    {
        return CreateBotAsync(strategy, StrategyFixtures.Element(strategy), balance, category, symbol);
    }

    /// <summary>A bot on the strategy of <paramref name="definition" />, otherwise as <see cref="CreateBotAsync(string, decimal, string, string)" />.</summary>
    public async Task<Guid> CreateBotAsync(string strategy, JsonElement definition, decimal balance = 1000m, string category = "spot", string symbol = "BTCUSDT")
    {
        HttpResponseMessage created = await Client.PostAsJsonAsync("/api/strategies", new { name = $"{strategy} {Guid.NewGuid():N}", definition });
        Guid strategyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var body = new JsonObject
        {
            ["name"] = $"BTC {strategy}",
            ["strategyId"] = strategyId,
            ["category"] = category,
            ["symbol"] = symbol,
            ["interval"] = "60",
            ["balance"] = balance,
            ["exchangeAccountId"] = Account
        };
        HttpResponseMessage response = await Client.PostAsJsonAsync("/api/bots", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task ActAsync(Guid bot, string action)
    {
        HttpResponseMessage response = await Client.PostAsync($"/api/bots/{bot}/{action}", null);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    public static string ClientId(Guid bot, int number)
    {
        return new ClientOrderId(bot, number).Code;
    }

    public async Task<LiveBot> LiveAsync(Guid bot)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetAsync(bot, CancellationToken.None);
    }

    public async Task<IReadOnlyList<LivePosition>> PositionsAsync(Guid bot)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetPositionsAsync(bot, CancellationToken.None);
    }

    public async Task<IReadOnlyList<LiveOrder>> OrdersAsync(Guid bot)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetOrdersAsync(bot, CancellationToken.None);
    }

    /// <summary>Moves the clock a second at a time until the condition holds, so the Workers poll and renew their leases.</summary>
    public async Task WaitAsync(Func<Task<bool>> condition, string what)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            if (await condition())
            {
                return;
            }

            Factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(25);
        }

        throw new TimeoutException($"Waited in vain for {what}.");
    }

    public Task WaitAsync(Func<bool> condition, string what)
    {
        return WaitAsync(() => Task.FromResult(condition()), what);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (WorkerHost worker in _workers)
        {
            await worker.DisposeAsync();
        }

        Client.Dispose();
        await Factory.DisposeAsync();
    }
}