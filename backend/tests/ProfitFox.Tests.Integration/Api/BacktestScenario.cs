using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.Backtests;
using ProfitFox.Domain.Entities.Backtests;
using ProfitFox.Domain.Entities.Backtests.Enums;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Tests.Integration.Infrastructure;
using ProfitFox.Tests.Shared.Fixtures.Strategies;

namespace ProfitFox.Tests.Integration.Api;

/// <summary>A strategy, a bot on <c>linear</c> / <c>BTCUSDT</c> at one hour and its backtests, in a running app.</summary>
public sealed class BacktestScenario(ProfitFoxFactory factory, HttpClient client)
{
    public static readonly Instrument BtcLinear = new Instrument(MarketCategory.Linear, "BTCUSDT");
    public static readonly InstrumentRules Rules = new InstrumentRules(0.01m, 0.001m, 0.001m, 1m);

    public Guid StrategyId { get; private set; }

    public Guid BotId { get; private set; }

    public async Task CreateBotAsync(string strategy, object? parameterValues = null)
    {
        factory.Bybit.Instruments.Add(("linear", "BTCUSDT"));
        HttpResponseMessage created = await client.PostAsJsonAsync("/api/strategies", new { name = strategy, definition = StrategyFixtures.Element(strategy) });
        StrategyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpResponseMessage bot = await client.PostAsJsonAsync(
            "/api/bots",
            new { name = $"{strategy} BTC", strategyId = StrategyId, parameterValues = parameterValues ?? new { }, category = "linear", symbol = "BTCUSDT", interval = "60", balance = 1000 });
        BotId = (await bot.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Hourly candles from <paramref name="from" />, one per close price, with the high and low 1 % off.</summary>
    public async Task StoreCandlesAsync(DateTimeOffset from, params decimal[] closes)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ICandleRepository>().UpsertAsync(BtcLinear, Interval.OneHour, Candles(from, closes), CancellationToken.None);
    }

    public static IReadOnlyList<Candle> Candles(DateTimeOffset from, params decimal[] closes)
    {
        return
        [
            .. closes.Select((close, hour) => new Candle(
                from.AddHours(hour),
                new Price(hour == 0 ? close : closes[hour - 1]),
                new Price(close * 1.01m),
                new Price(close * 0.99m),
                new Price(close),
                new Volume(1m),
                new Volume(close)))
        ];
    }

    /// <summary>Saves a pending backtest of the bot and queues it, as creating it through the API does.</summary>
    public async Task<Backtest> QueueAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyDictionary<string, ParameterSpec>? parameters = null)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IStrategyLibraryRepository library = scope.ServiceProvider.GetRequiredService<IStrategyLibraryRepository>();
        Bot bot = (await library.GetBotAsync(BotId, CancellationToken.None))!;
        StrategyVersion version = (await library.GetVersionAsync(StrategyId, (await library.GetLatestVersionNumberAsync(StrategyId, CancellationToken.None))!.Value, CancellationToken.None))!;
        var policy = new BacktestPolicy(10000, new Dictionary<MarketCategory, FeeRates> { [MarketCategory.Linear] = new FeeRates(0.02m, 0.055m) });
        var backtest = Backtest.Create(
            Guid.NewGuid(),
            bot,
            version,
            from,
            to,
            parameters ?? new Dictionary<string, ParameterSpec>(),
            new SimulationRequest(null, null, null, null, null),
            policy,
            Rules,
            factory.Time.GetUtcNow());
        await scope.ServiceProvider.GetRequiredService<IBacktestRepository>().AddAsync(backtest, CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<IBacktestQueue>().EnqueueAsync(backtest.Id, CancellationToken.None);

        return backtest;
    }

    public async Task<Backtest> GetAsync(Guid id)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IBacktestRepository>().GetAsync(id, CancellationToken.None))!;
    }

    public async Task<IReadOnlyList<BacktestResult>> ResultsAsync(Backtest backtest)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IBacktestRepository repository = scope.ServiceProvider.GetRequiredService<IBacktestRepository>();
        var results = new List<BacktestResult>();

        foreach (long combination in await repository.GetCalculatedCombinationsAsync(backtest.Id, CancellationToken.None))
        {
            results.Add((await repository.GetResultAsync(backtest.Id, combination, CancellationToken.None))!);
        }

        return results;
    }

    /// <summary>Waits for the status, moving the app's clock a second at a time so that the waits of the backtest pass.</summary>
    public async Task<Backtest> WaitForAsync(Guid id, params BacktestStatus[] statuses)
    {
        var waiting = Stopwatch.StartNew();

        while (true)
        {
            Backtest backtest = await GetAsync(id);

            if (statuses.Contains(backtest.Status) || waiting.Elapsed > TimeSpan.FromSeconds(30))
            {
                return backtest;
            }

            factory.Time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
        }
    }

    public Task<Backtest> WaitUntilFinishedAsync(Guid id)
    {
        return WaitForAsync(id, BacktestStatus.Succeeded, BacktestStatus.Failed, BacktestStatus.Cancelled);
    }
}