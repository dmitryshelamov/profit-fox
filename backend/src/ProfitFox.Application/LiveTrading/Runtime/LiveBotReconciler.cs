using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Transactions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>
///     Compares the books of a live bot and of its neighbours, the bots that share its holding on the exchange, with the exchange,
///     and stops them all when the books differ. A session calls it after it caught up with the exchange.
/// </summary>
internal sealed class LiveBotReconciler(
    ILiveBotRepository repository,
    IStrategyLibraryRepository library,
    IExchangeLedger ledger,
    IInstrumentCatalog catalog,
    ITransactionExecutor executor,
    ILiveTradingJournal journal,
    WorkerIdentity worker,
    TimeProvider time)
{
    /// <summary>The bots the books of the bot of <paramref name="context" /> are compared with, and what differs.</summary>
    public async Task<(IReadOnlyList<Guid> Bots, IReadOnlyList<string> Differences)> CompareAsync(LiveBotContext context, CancellationToken cancellationToken)
    {
        Bot bot = context.Bot;
        IReadOnlyList<Bot> neighbours = await NeighboursAsync(context, cancellationToken);
        var holdings = new List<BotHolding>();
        var pending = new HashSet<ClientOrderId>();

        foreach (Bot neighbour in neighbours)
        {
            LiveBot live = await repository.GetAsync(neighbour.Id, cancellationToken);

            if (live.StrategyVersion is not { } number)
            {
                continue;
            }

            StrategyDirection direction = neighbour.Id == bot.Id
                ? context.Direction
                : (await library.GetVersionAsync(neighbour.StrategyId, number, cancellationToken))!.Snapshot.Definition.Direction;
            LivePosition? position = (await repository.GetPositionsAsync(neighbour.Id, cancellationToken)).FirstOrDefault();
            LiveOrder[] unfinished = [.. (await repository.GetOrdersAsync(neighbour.Id, cancellationToken)).Where(order => order.Unfinished)];
            pending.UnionWith(unfinished.Where(order => order.Status == LiveOrderStatus.PendingSubmit).Select(order => order.ClientId));
            holdings.Add(new BotHolding(
                neighbour.Id,
                direction,
                position?.ClosedAt is null ? position?.Quantity ?? 0m : 0m,
                [.. unfinished.Where(order => order.Status != LiveOrderStatus.PendingSubmit).Select(order => order.Number)]));
        }

        var ids = holdings.Select(holding => holding.BotId).ToHashSet();
        ClientOrderId[] open =
        [
            .. (await ledger.GetOpenOrdersAsync(context.Key, bot.Instrument, cancellationToken))
                .Select(order => ClientOrderId.TryParse(order.ClientId, out ClientOrderId? id) ? id : null)
                .OfType<ClientOrderId>()
                .Where(id => ids.Contains(id.BotId) && !pending.Contains(id))
        ];
        var finished = new List<ClientOrderId>();

        foreach (BotHolding holding in holdings)
        {
            foreach (ClientOrderId id in holding.ActiveOrders.Select(number => new ClientOrderId(holding.BotId, number)).Where(id => !open.Contains(id)))
            {
                if (await ledger.FindOrderAsync(context.Key, bot.Instrument, id.Code, cancellationToken) is { State: not ExchangeOrderState.Open })
                {
                    finished.Add(id);
                }
            }
        }

        decimal held = await ledger.GetHoldingAsync(context.Key, bot.Instrument, context.BaseCoin, cancellationToken);
        IReadOnlyList<string> differences = Reconciliation.Check(bot.Instrument.Category, context.Conditions.Rules.QuantityStep, holdings, new ExchangeHoldings(held, open, finished));

        return ([.. holdings.Select(holding => holding.BotId)], differences);
    }

    /// <summary>
    ///     Stops every working bot of the difference with its reason; a fix is never guessed. The bot of <paramref name="leased" />
    ///     is locked by its lease. Returns that bot as written, or <c>null</c> when it is not among <paramref name="bots" />.
    /// </summary>
    public async Task<LiveBot?> FailAllAsync(LeasedBot leased, IEnumerable<Guid> bots, string reason, CancellationToken cancellationToken)
    {
        Guid own = leased.Bot.BotId;
        journal.Failed(own, reason);

        return await executor.ExecuteAsync(
            async token =>
            {
                LiveBot? written = null;

                foreach (Guid id in bots)
                {
                    LiveBot bot = id == own
                        ? await repository.LockLeasedAsync(own, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException()
                        : (await repository.LockAsync(id, time.GetUtcNow(), token))!;

                    if (bot.Status is LiveBotStatus.Running or LiveBotStatus.Paused)
                    {
                        bot = bot.Fail(reason);
                        await repository.SaveAsync(bot, time.GetUtcNow(), token);
                    }

                    if (id == own)
                    {
                        written = bot;
                    }
                }

                return written;
            },
            cancellationToken);
    }

    /// <summary>The bots of the account on the instrument of the bot, on the spot on any instrument of its coin.</summary>
    private async Task<IReadOnlyList<Bot>> NeighboursAsync(LiveBotContext context, CancellationToken cancellationToken)
    {
        Bot bot = context.Bot;
        IReadOnlyList<Bot> bots = [.. (await library.GetBotsAsync(cancellationToken)).Where(other => other.ExchangeAccountId == bot.ExchangeAccountId)];

        if (bot.Instrument.Category == MarketCategory.Linear)
        {
            return [.. bots.Where(other => other.Instrument == bot.Instrument)];
        }

        var coins = (await catalog.ListAsync(MarketCategory.Spot, cancellationToken)).ToDictionary(item => item.Symbol, item => item.BaseCoin);

        return [.. bots.Where(other => other.Instrument.Category == MarketCategory.Spot && coins.GetValueOrDefault(other.Instrument.Symbol) == context.BaseCoin)];
    }
}