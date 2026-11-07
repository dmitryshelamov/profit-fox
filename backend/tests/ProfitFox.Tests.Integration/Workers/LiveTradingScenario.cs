using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using NSubstitute;

using ProfitFox.Application.Ports.ExchangeAccounts;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Infrastructure.Persistence.Postgres.Dapper.Repositories.Ideas;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;
using ProfitFox.Tests.Shared.Generators.Domain.Entity.Ideas;

using Shouldly;

namespace ProfitFox.Tests.Integration.Workers;

/// <summary>
///     A trading Worker on the API's database with one exchange account on <see cref="FakeExchange" />: the history of BTCUSDT on
///     the spot closes at 100 every hour up to 11:00, and the clock of the app stands at 12:30.
/// </summary>
public sealed class LiveTradingScenario : IAsyncDisposable
{
    public static readonly DateTimeOffset LastOpen = DateTimeOffset.Parse("2024-03-10T11:00:00Z");

    /// <summary>The instrument of the bots of a scenario, on the spot or on the linear market.</summary>
    public static readonly Instrument Spot = new Instrument(MarketCategory.Spot, "BTCUSDT");

    public static readonly Instrument Linear = new Instrument(MarketCategory.Linear, "BTCUSDT");

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

    /// <param name="bybitSocketAddress">The streams of Bybit the API's chart hub connects to; without it the API's streams use the live Bybit.</param>
    public static async Task<LiveTradingScenario> CreateAsync(string? bybitSocketAddress = null)
    {
        var factory = new ProfitFoxFactory(IntegrationEnvironment.ConnectionString) { Workers = 0, BybitSocketAddress = bybitSocketAddress };
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
        scenario.Exchange.Clock = factory.Time;
        factory.KeyInspector.InspectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExchangeKeyInfo(100, false, true, false, new HashSet<MarketCategory> { MarketCategory.Spot, MarketCategory.Linear }));
        HttpResponseMessage account = await scenario.Client.PostAsJsonAsync("/api/exchange-accounts", new { name = "Основной", apiKey = "api-key", apiSecret = "api-secret" });
        account.StatusCode.ShouldBe(HttpStatusCode.Created);
        scenario.Account = (await account.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        return scenario;
    }

    /// <summary>
    ///     A trading Worker (with <paramref name="inspectKeys" /> one that asks the fake inspector of the factory what a key may do) whose exchange is <see cref="Exchange" />, with <paramref name="settings" /> over its own. With
    ///     <paramref name="bybitSocketAddress" /> its candles and account events come from the Bybit streams at that address, not
    ///     from <see cref="Exchange" />.
    /// </summary>
    public WorkerHost StartWorker(Serilog.Core.ILogEventSink? logSink = null, IReadOnlyDictionary<string, string?>? settings = null, string? bybitSocketAddress = null, bool inspectKeys = false)
    {
        var workerSettings = new Dictionary<string, string?>
        {
            ["Trading:Enabled"] = "true",
            ["ExchangeAccounts:MasterKey"] = ProfitFoxFactory.TestMasterKey
        };

        if (bybitSocketAddress is not null)
        {
            workerSettings["Bybit:SocketAddress"] = bybitSocketAddress;
        }

        foreach ((string key, string? value) in settings ?? new Dictionary<string, string?>())
        {
            workerSettings[key] = value;
        }

        WorkerHost worker = Factory.StartWorker(
            services =>
            {
                services.RemoveAll<IExchangeLedger>();
                services.RemoveAll<IOrderGateway>();
                services.AddSingleton<IExchangeLedger>(Exchange);
                services.AddSingleton<IOrderGateway>(Exchange);

                if (inspectKeys)
                {
                    // The entries of the autos ask what the key may do, as the API does for a manual entry.
                    services.RemoveAll<IExchangeKeyInspector>();
                    services.AddSingleton(Factory.KeyInspector);
                }

                if (bybitSocketAddress is null)
                {
                    services.RemoveAll<IExchangeStreams>();
                    services.AddSingleton<IExchangeStreams>(Exchange);
                }
            },
            workerSettings: workerSettings,
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

    public Task SetLiveAsync(Guid bot, Func<LiveBot, LiveBot> change)
    {
        return InUnitAsync(async (repository, cancellationToken) =>
        {
            LiveBot live = (await repository.LockAsync(bot, Factory.Time.GetUtcNow(), cancellationToken))!;
            await repository.SaveAsync(change(live), Factory.Time.GetUtcNow(), cancellationToken);
        });
    }

    public Task SavePositionAsync(Guid bot, LivePosition position)
    {
        return InUnitAsync((repository, cancellationToken) => repository.SavePositionAsync(position, cancellationToken));
    }

    private async Task InUnitAsync(Func<ILiveBotRepository, CancellationToken, Task> work)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        ILiveBotRepository repository = scope.ServiceProvider.GetRequiredService<ILiveBotRepository>();
        await scope.ServiceProvider.GetRequiredService<ProfitFox.Application.Transactions.ITransactionExecutor>().ExecuteAsync(
            async cancellationToken =>
            {
                await work(repository, cancellationToken);

                return 0;
            },
            CancellationToken.None);
    }

    public async Task<IReadOnlyList<LiveFill>> FillsAsync(Guid bot)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ILiveBotRepository>().GetFillsAsync(bot, CancellationToken.None);
    }

    /// <summary>
    ///     A trade by an idea saved as the API saves it: waiting for its entry, with the entry order saved to be sent. The idea is a market
    ///     <c>TwoR</c> long on BTCUSDT on the linear market of 15 minutes that fired a few minutes ago: entry 105, stop 100, target 115.
    /// </summary>
    public async Task<IdeaTrade> CreateTradeAsync(IdeaType type = IdeaType.TwoR, IdeaSide side = IdeaSide.Long, decimal quantity = 0.47m, Instrument? instrument = null, decimal risk = 2m, Guid? autoId = null)
    {
        DateTimeOffset now = Factory.Time.GetUtcNow();
        Idea idea = IdeaGenerator.Generate(now.AddMinutes(-5), instrument: instrument ?? Linear, type: type, side: side);
        var trade = IdeaTrade.Create(Guid.NewGuid(), idea, Account, 105m, new IdeaTradeSize(quantity, quantity * 105m, risk, risk * 2m, 2m), now, autoId: autoId);
        bool pullback = type == IdeaType.Pullback;
        var entry = IdeaTradeOrder.NewEntry(
            trade.Id,
            1,
            side == IdeaSide.Long ? OrderSide.Buy : OrderSide.Sell,
            pullback ? OrderType.Limit : OrderType.Market,
            TimeInForce.GoodTilCancelled,
            pullback ? trade.EntryPrice : null,
            quantity,
            new Protection(trade.Target, trade.Stop),
            now);
        trade = trade with { LastOrderNumber = 1 };
        await InTradesAsync((repository, cancellationToken) => repository.CreateAsync(trade, entry, now, cancellationToken));

        return trade;
    }

    /// <summary>An idea of the scanner on the instrument, long unless told otherwise, that fired a few minutes ago, with the levels given.</summary>
    public async Task<Guid> SaveIdeaAsync(
        decimal entry,
        decimal stop,
        decimal target,
        IdeaType type = IdeaType.TwoR,
        IdeaStatus status = IdeaStatus.AwaitingEntry,
        Instrument? instrument = null,
        IdeaSide side = IdeaSide.Long)
    {
        Idea idea = IdeaGenerator.Generate(Factory.Time.GetUtcNow().AddMinutes(-5), status: status, instrument: instrument ?? Linear, type: type, side: side);
        idea = idea with { Track = idea.Track with { Plan = new IdeaPlan(type, side, entry, stop, target) } };
        await using var database = Npgsql.NpgsqlDataSource.Create(Factory.ConnectionString);
        await new IdeaRepository(database).SaveAsync([idea], [], CancellationToken.None);

        return idea.Id;
    }

    /// <summary>The bucket over 90 days of the ideas <see cref="SaveIdeaAsync" /> makes, a long of the setup and the interval of the generator, replaced by the statistics given.</summary>
    public async Task SaveBucketAsync(BucketStatistics statistics, IdeaType type = IdeaType.TwoR)
    {
        var key = new IdeaBucketKey(IdeaSetup.Impulse, type, Interval.FifteenMinutes, IdeaSide.Long, MarketCategory.Linear);
        await using var database = Npgsql.NpgsqlDataSource.Create(Factory.ConnectionString);
        await new IdeaBucketRepository(database).ReplaceAsync(IdeasEngineVersion.Current, [new IdeaBucket(key, 90, statistics)], null, CancellationToken.None);
    }

    /// <summary>Writes how the idea ended on the trades made of it, as the scanner does when the idea closes.</summary>
    public async Task WriteIdeaOutcomeAsync(Guid idea, TradedIdeaOutcome outcome)
    {
        await using var database = Npgsql.NpgsqlDataSource.Create(Factory.ConnectionString);
        await new IdeaRepository(database).SaveAsync([], [], [new ClosedIdea(idea, outcome)], CancellationToken.None);
    }

    /// <summary>The last price of the ticker of the pair on Bybit.</summary>
    public void SetPrice(string price, Instrument? instrument = null)
    {
        Instrument pair = instrument ?? Linear;
        Factory.Bybit.Tickers[(pair.Category == MarketCategory.Spot ? "spot" : "linear", pair.Symbol)] = (price, price, price, "1", "0.0001");
    }

    public async Task<IdeaTrade> TradeAsync(Guid trade)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return (await scope.ServiceProvider.GetRequiredService<IIdeaTradeRepository>().GetAsync(trade, CancellationToken.None))!;
    }

    public async Task<IReadOnlyList<IdeaTradeOrder>> TradeOrdersAsync(Guid trade)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IIdeaTradeRepository>().GetOrdersAsync(trade, CancellationToken.None);
    }

    public async Task<IReadOnlyList<IdeaTradeChild>> TradeChildrenAsync(Guid trade)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IIdeaTradeRepository>().GetChildrenAsync(trade, CancellationToken.None);
    }

    public async Task<IReadOnlyList<IdeaTradeFill>> TradeFillsAsync(Guid trade)
    {
        using IServiceScope scope = Factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IIdeaTradeRepository>().GetFillsAsync(trade, CancellationToken.None);
    }

    /// <summary>Changes a trade as the API does: under the lock of its row.</summary>
    public Task ChangeTradeAsync(Guid trade, Func<IdeaTrade, IdeaTrade> change)
    {
        return InTradesAsync(async (repository, cancellationToken) =>
        {
            IdeaTrade locked = (await repository.LockAsync(trade, cancellationToken))!;
            await repository.SaveAsync(change(locked), Factory.Time.GetUtcNow(), cancellationToken);
        });
    }

    private async Task InTradesAsync(Func<IIdeaTradeRepository, CancellationToken, Task> work)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        IIdeaTradeRepository repository = scope.ServiceProvider.GetRequiredService<IIdeaTradeRepository>();
        await scope.ServiceProvider.GetRequiredService<ProfitFox.Application.Transactions.ITransactionExecutor>().ExecuteAsync(
            async cancellationToken =>
            {
                await work(repository, cancellationToken);

                return 0;
            },
            CancellationToken.None);
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