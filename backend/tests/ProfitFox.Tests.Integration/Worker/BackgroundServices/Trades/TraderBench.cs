using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitFox.Application.Commands.Trades.OpenTrade;
using ProfitFox.Domain.Entities.Accounts;
using ProfitFox.Domain.Entities.Ideas;
using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Tests.Integration.Application.Commands.Trades;
using ProfitFox.Tests.Integration.Exchange;
using ProfitFox.Tests.Integration.MarketData;
using ProfitFox.Tests.Shared.Logging;

namespace ProfitFox.Tests.Integration.Worker.BackgroundServices.Trades;

/// <summary>
///     The bench of a test of the trading Worker (change take-idea-trades, design.md, decision 15): the API side of
///     <see cref="TradeBench" /> — the base, <see cref="FakeBybit" />, the account «Основной», the settings — and trading
///     Workers of the test process on the same fake, with the intervals of <see cref="WorkerHost.FastTrading" />. The trade of a
///     test is entered as the button enters it, and the Worker sends it.
/// </summary>
internal sealed class TraderBench : IAsyncDisposable
{
    private readonly List<WorkerHost> _workers = [];

    private TraderBench(TradeBench api, MarketDataServer server)
    {
        Api = api;
        Server = server;
    }

    public TradeBench Api { get; }

    public MarketDataServer Server { get; }

    public FakeBybit Bybit => Api.Bybit;

    public FakeBybit.Account Main => Bybit.Key(TradeBench.MainKey);

    public ExchangeAccount Account => Api.Main;

    public ISender Sender => Api.Sender;

    public ListLogger Log { get; } = new ListLogger();

    public ITradeRepository Trades => Api.Services.GetRequiredService<ITradeRepository>();

    /// <summary>
    ///     The bench with the limits of spec trade-risk «Настройки сохранены» and <paramref name="entryWait" /> as the wait for
    ///     the entry — written past the rules of the settings, which keep it from 10 seconds, so a test does not wait for 30 —
    ///     and no margin before the end of an idea.
    /// </summary>
    public static async Task<TraderBench> Start(TimeSpan? entryWait = null, decimal? maxDayLoss = 50m, int stopAttempts = 3)
    {
        TradeBench api = await TradeBench.Start(trader: false);
        // No margin before the end of an idea either: a test enters an idea that ends in seconds.
        await api.Services.GetRequiredService<ITradeSettingsRepository>().SaveAsync(
            TradeBench.Limits with { EntryWait = entryWait ?? TimeSpan.FromSeconds(30), MaxDayLoss = maxDayLoss, StopAttempts = stopAttempts, ExpiryMargin = TimeSpan.Zero },
            CancellationToken.None);
        return new TraderBench(api, await MarketDataServer.Start());
    }

    /// <summary>
    ///     Starts a trading Worker and waits until one holds the lease of the trader.
    /// </summary>
    public async Task<WorkerHost> Worker(string name = "trader-1", IReadOnlyDictionary<string, string?>? trading = null, bool wait = true)
    {
        WorkerHost worker = await WorkerHost.Start(Server, name, runs: false, bybit: Bybit, trading: trading, log: Log);
        _workers.Add(worker);
        if (wait)
        {
            await Poll.Until("a trading Worker holds the lease", async () => (await Api.Services.GetRequiredService<ITraderLeaseRepository>().StateAsync(CancellationToken.None))?.Alive == true);
        }

        return worker;
    }

    /// <summary>
    ///     A trading Worker that works for the API and does nothing: the lease of «nobody» for a minute, so an entry is written
    ///     and no order goes.
    /// </summary>
    public async Task Pretend()
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO trader_lease (name, worker, until) VALUES ('trader', 'nobody', now() + interval '1 minute') ON CONFLICT (name) DO UPDATE SET worker = 'nobody', until = now() + interval '1 minute'");
    }

    /// <summary>
    ///     The lease of <see cref="Pretend" /> runs out at once.
    /// </summary>
    public async Task Forget()
    {
        await using NpgsqlConnection connection = await BenchDatabase.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE trader_lease SET until = now() WHERE worker = 'nobody'");
    }

    public async Task Stop(WorkerHost worker)
    {
        _workers.Remove(worker);
        await worker.DisposeAsync();
    }

    public Task<Idea> Idea(string symbol = "SOLUSDT", EntrySide side = EntrySide.Long, double entry = 236.10, double? stop = 231.38, double? target = 245.54, TimeSpan? left = null)
    {
        return Api.Idea(symbol, side, entry, stop, target, left);
    }

    /// <summary>
    ///     Enters the idea as the button does.
    /// </summary>
    public Task<Trade> Enter(Idea idea, Guid? account = null, decimal? amount = null)
    {
        return Sender.Send(new OpenTradeCommand(idea.Id, account, amount));
    }

    /// <summary>
    ///     Enters a long on <c>SOLUSDT</c> and waits until it is open.
    /// </summary>
    public async Task<Trade> Opened(double? target = 245.54, TimeSpan? left = null)
    {
        Trade trade = await Enter(await Idea(target: target, left: left));
        return await Until(trade.Id, TradeState.Open);
    }

    public async Task<Trade> Read(Guid id)
    {
        return (await Trades.FindAsync(id, CancellationToken.None))!;
    }

    /// <summary>
    ///     Waits until the trade is in <paramref name="state" /> and gives it.
    /// </summary>
    public async Task<Trade> Until(Guid id, TradeState state, TimeSpan? timeout = null)
    {
        await Poll.Until($"trade {id} is {state}", async () => (await Read(id)).State == state, timeout ?? TimeSpan.FromSeconds(15));
        return await Read(id);
    }

    public async Task<IReadOnlyList<TradeOrder>> Orders(Guid id)
    {
        return (await Trades.HoldingAsync(CancellationToken.None)).FirstOrDefault(book => book.Trade.Id == id)?.Orders ?? [];
    }

    public async Task<string?> Feed()
    {
        return (await Api.Services.GetRequiredService<IExchangeAccountRepository>().FindAsync(Account.Id, CancellationToken.None))!.Feed;
    }

    public async Task<TradeHalt> Halt()
    {
        return await Api.Services.GetRequiredService<ITradeHaltRepository>().GetAsync(CancellationToken.None);
    }

    public int CallsOf(string call)
    {
        return Bybit.CallsNow().Count(found => found.Call == call);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (WorkerHost worker in _workers)
        {
            await worker.DisposeAsync();
        }

        await Api.DisposeAsync();
        await Server.DisposeAsync();
    }
}
