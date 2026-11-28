using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitFox.Application.Ports.Security;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Integration.Exchange;
using ProfitFox.Tests.Shared.Generators.Domain.Entities.Ideas;

namespace ProfitFox.Tests.Integration.Application.Commands.Trades;

/// <summary>
///     The bench of a test of the trades (change take-idea-trades): the services over the emptied test database with
///     <see cref="FakeBybit" /> in place of Bybit, an account «Основной» with a key the fake knows, the settings of spec
///     trade-risk «Настройки сохранены», a trading Worker that holds its lease, and ideas written as the scanner writes them.
/// </summary>
internal sealed class TradeBench : IAsyncDisposable
{
    public const string MainKey = "key-main";

    public static readonly TradeSettings Limits = new TradeSettings
    {
        DefaultAmount = 100m,
        Leverage = 3,
        MaxAmount = 200m,
        MaxOpenTrades = 5,
        MaxTotalAmount = 1000m,
        MaxLossPercent = 3m,
        MaxDayLoss = 50m,
        SavedAt = DateTimeOffset.UtcNow
    };

    private TradeBench(ServiceProvider services, FakeBybit bybit, ExchangeAccount main)
    {
        Services = services;
        Bybit = bybit;
        Main = main;
    }

    public ServiceProvider Services { get; }

    public FakeBybit Bybit { get; }

    public ExchangeAccount Main { get; }

    public ISender Sender => Services.GetRequiredService<ISender>();

    public static async Task<TradeBench> Start(bool limits = true, bool trader = true)
    {
        await BenchDatabase.Reset();
        FakeBybit bybit = new FakeBybit().Pair("SOLUSDT", 237.40m).Pair("XRPUSDT", 1.4126m, step: 1m, minQuantity: 1m, tick: 0.0001m).Pair("BTCUSDT", 100000m, step: 0.001m, minQuantity: 0.001m, tick: 0.1m);
        ServiceProvider services = BenchServices.Start(bybit: bybit);
        var bench = new TradeBench(services, bybit, await AddAccount(services, "Основной", MainKey, 104857321));
        if (limits)
        {
            await services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(Limits, CancellationToken.None);
        }

        if (trader)
        {
            await services.GetRequiredService<ITraderLeaseRepository>().TakeAsync("trader-test", TimeSpan.FromMinutes(5), CancellationToken.None);
        }

        return bench;
    }

    public static async Task<ExchangeAccount> AddAccount(IServiceProvider services, string name, string apiKey, long uid)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var account = new ExchangeAccount(Guid.NewGuid(), name, uid, apiKey, now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond)), "ISOLATED_MARGIN");
        await services.GetRequiredService<IExchangeAccountRepository>().AddAsync(account, services.GetRequiredService<ISecretProtector>().Protect(account.Id, $"secret-of-{apiKey}"), CancellationToken.None);
        return account;
    }

    /// <summary>
    ///     An open idea entered at <paramref name="entry" /> with the stop and the target as prices, ending in
    ///     <paramref name="left" /> from now, as the scanner keeps it.
    /// </summary>
    public async Task<Idea> Idea(string symbol = "SOLUSDT", EntrySide side = EntrySide.Long, double entry = 236.10, double? stop = 231.38, double? target = 245.54, TimeSpan? left = null)
    {
        double direction = side == EntrySide.Long ? 1 : -1;
        // The store keeps the length of an idea in whole minutes: the moment is put so that the end is the one asked for.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset until = now + (left ?? TimeSpan.FromHours(3));
        until = until.AddTicks(-(until.Ticks % TimeSpan.TicksPerMicrosecond));
        var after = TimeSpan.FromMinutes(Math.Ceiling((until - now + TimeSpan.FromHours(1)).TotalMinutes));
        DateTimeOffset moment = until - after;
        Idea idea = IdeaGenerator.Generate(
            name: "breakout-long",
            version: 7,
            symbol: symbol,
            timeframe: TimeSpan.FromHours(1),
            side: side,
            moment: moment,
            after: after,
            stop: stop is { } s ? (1 - s / entry) * direction : null,
            target: target is { } t ? (t / entry - 1) * direction : null,
            close: (decimal)entry);
        IIdeaRepository ideas = Services.GetRequiredService<IIdeaRepository>();
        await ideas.AddAsync(idea, CancellationToken.None);
        idea.EntryPrice = entry;
        idea.EntryRaw = entry;
        idea.EntryTime = moment.AddMinutes(1);
        await ideas.SaveAsync(idea, CancellationToken.None);
        return idea;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
    }
}
