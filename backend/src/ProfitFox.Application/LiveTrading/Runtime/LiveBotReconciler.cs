using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Transactions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>
///     Compares the books of a live bot or of a trade by an idea and of its neighbours, the bots and the trades that share its holding on
///     the exchange, with the exchange, and stops them all when the books differ. A session calls it after it caught up with the exchange.
/// </summary>
internal sealed class LiveBotReconciler(
    ILiveBotRepository repository,
    IIdeaTradeRepository ideaTrades,
    IStrategyLibraryRepository library,
    IExchangeLedger ledger,
    IInstrumentCatalog catalog,
    ITransactionExecutor executor,
    ILiveTradingJournal journal,
    WorkerIdentity worker,
    TimeProvider time)
{
    /// <summary>The bots and the trades the books of the bot of <paramref name="context" /> are compared with, and what differs.</summary>
    public async Task<(IReadOnlyList<Guid> Bots, IReadOnlyList<Guid> Trades, IReadOnlyList<string> Differences)> CompareAsync(LiveBotContext context, CancellationToken cancellationToken)
    {
        Bot bot = context.Bot;

        return await CompareAsync(bot.ExchangeAccountId, bot.Instrument, context.Key, context.BaseCoin, context.Conditions.Rules.QuantityStep, (bot.Id, context.Direction), cancellationToken);
    }

    /// <summary>The bots and the trades the books of the trade of an account are compared with, and what differs.</summary>
    public Task<(IReadOnlyList<Guid> Bots, IReadOnlyList<Guid> Trades, IReadOnlyList<string> Differences)> CompareAsync(
        IdeaTrade trade,
        ExchangeKey key,
        string baseCoin,
        InstrumentRules rules,
        CancellationToken cancellationToken)
    {
        return CompareAsync(trade.AccountId, trade.Instrument, key, baseCoin, rules.QuantityStep, null, cancellationToken);
    }

    private async Task<(IReadOnlyList<Guid> Bots, IReadOnlyList<Guid> Trades, IReadOnlyList<string> Differences)> CompareAsync(
        Guid? accountId,
        Instrument instrument,
        ExchangeKey key,
        string baseCoin,
        decimal quantityStep,
        (Guid Id, StrategyDirection Direction)? own,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Bot> neighbours = await NeighboursAsync(accountId, instrument, baseCoin, cancellationToken);
        var holdings = new List<BotHolding>();
        var pending = new HashSet<ClientOrderId>();

        foreach (Bot neighbour in neighbours)
        {
            LiveBot live = await repository.GetAsync(neighbour.Id, cancellationToken);

            if (live.StrategyVersion is not { } number)
            {
                continue;
            }

            StrategyDirection direction = neighbour.Id == own?.Id
                ? own.Value.Direction
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

        var tradeHoldings = new List<TradeHolding>();
        var pendingTrade = new HashSet<IdeaTradeOrderId>();

        foreach (IdeaTrade trade in await TradeNeighboursAsync(accountId, instrument, baseCoin, cancellationToken))
        {
            IdeaTradeOrder[] unfinished = [.. (await ideaTrades.GetOrdersAsync(trade.Id, cancellationToken)).Where(order => order.Unfinished)];
            pendingTrade.UnionWith(unfinished.Where(order => order.Status == LiveOrderStatus.PendingSubmit).Select(order => order.ClientId));
            tradeHoldings.Add(new TradeHolding(
                trade.Id,
                trade.Side == IdeaSide.Long ? StrategyDirection.Long : StrategyDirection.Short,
                trade.HeldQuantity,
                [.. unfinished.Where(order => order.Status != LiveOrderStatus.PendingSubmit).Select(order => order.Number)]));
        }

        var ids = holdings.Select(holding => holding.BotId).ToHashSet();
        var tradeIds = tradeHoldings.Select(holding => holding.TradeId).ToHashSet();
        ExchangeOrder[] onExchange = [.. await ledger.GetOpenOrdersAsync(key, instrument, cancellationToken)];
        ClientOrderId[] open =
        [
            .. onExchange
                .Select(order => ClientOrderId.TryParse(order.ClientId, out ClientOrderId? id) ? id : null)
                .OfType<ClientOrderId>()
                .Where(id => ids.Contains(id.BotId) && !pending.Contains(id))
        ];
        IdeaTradeOrderId[] openTrade =
        [
            .. onExchange
                .Select(order => IdeaTradeOrderId.TryParse(order.ClientId, out IdeaTradeOrderId? id) ? id : null)
                .OfType<IdeaTradeOrderId>()
                .Where(id => tradeIds.Contains(id.TradeId) && !pendingTrade.Contains(id))
        ];
        var finished = new List<ClientOrderId>();
        var finishedTrade = new List<IdeaTradeOrderId>();

        foreach (BotHolding holding in holdings)
        {
            foreach (ClientOrderId id in holding.ActiveOrders.Select(number => new ClientOrderId(holding.BotId, number)).Where(id => !open.Contains(id)))
            {
                if (await ledger.FindOrderAsync(key, instrument, id.Code, cancellationToken) is { State: not ExchangeOrderState.Open })
                {
                    finished.Add(id);
                }
            }
        }

        foreach (TradeHolding holding in tradeHoldings)
        {
            foreach (IdeaTradeOrderId id in holding.ActiveOrders.Select(number => new IdeaTradeOrderId(holding.TradeId, number)).Where(id => !openTrade.Contains(id)))
            {
                if (await ledger.FindOrderAsync(key, instrument, id.Code, cancellationToken) is { State: not ExchangeOrderState.Open })
                {
                    finishedTrade.Add(id);
                }
            }
        }

        decimal held = await ledger.GetHoldingAsync(key, instrument, baseCoin, cancellationToken);
        IReadOnlyList<string> differences = Reconciliation.Check(
            instrument.Category,
            quantityStep,
            holdings,
            new ExchangeHoldings(held, open, finished, openTrade, finishedTrade),
            tradeHoldings);

        return ([.. holdings.Select(holding => holding.BotId)], [.. tradeHoldings.Select(holding => holding.TradeId)], differences);
    }

    /// <summary>
    ///     Stops every working bot of the difference with its reason, and fails every open trade by an idea with it; a fix is never guessed,
    ///     and the stop and the target of a trade stay on the exchange. The bot of <paramref name="leased" /> is locked by its lease. Returns
    ///     that bot as written, or <c>null</c> when it is not among <paramref name="bots" />.
    /// </summary>
    public async Task<LiveBot?> FailAllAsync(LeasedBot leased, IEnumerable<Guid> bots, IEnumerable<Guid> trades, string reason, CancellationToken cancellationToken)
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

                await FailTradesAsync(trades, null, reason, token);

                return written;
            },
            cancellationToken);
    }

    /// <summary>
    ///     As the other overload, for a trade by an idea that found the difference: the bots are locked first, then the trades, the same
    ///     order, so the two never wait for each other in turns. Returns the trade of <paramref name="leased" /> as written.
    /// </summary>
    public async Task<IdeaTrade?> FailAllAsync(LeasedIdeaTrade leased, IEnumerable<Guid> bots, IEnumerable<Guid> trades, string reason, CancellationToken cancellationToken)
    {
        journal.Failed(leased.Trade.Id, reason);

        return await executor.ExecuteAsync(
            async token =>
            {
                foreach (Guid id in bots)
                {
                    LiveBot bot = (await repository.LockAsync(id, time.GetUtcNow(), token))!;

                    if (bot.Status is LiveBotStatus.Running or LiveBotStatus.Paused)
                    {
                        await repository.SaveAsync(bot.Fail(reason), time.GetUtcNow(), token);
                    }
                }

                return await FailTradesAsync(trades, leased, reason, token);
            },
            cancellationToken);
    }

    private async Task<IdeaTrade?> FailTradesAsync(IEnumerable<Guid> trades, LeasedIdeaTrade? own, string reason, CancellationToken cancellationToken)
    {
        IdeaTrade? written = null;

        foreach (Guid id in trades.Order())
        {
            IdeaTrade? trade = own?.Trade.Id == id
                ? await ideaTrades.LockLeasedAsync(id, worker.Id, own.Epoch, cancellationToken) ?? throw new LeaseLostException()
                : await ideaTrades.LockAsync(id, cancellationToken);

            if (trade is null)
            {
                continue;
            }

            trade = trade.Fail(reason, time.GetUtcNow());
            await ideaTrades.SaveAsync(trade, time.GetUtcNow(), cancellationToken);

            if (own?.Trade.Id == id)
            {
                written = trade;
            }
        }

        return written;
    }

    /// <summary>The bots of the account on the instrument, on the spot on any instrument of its coin.</summary>
    private async Task<IReadOnlyList<Bot>> NeighboursAsync(Guid? accountId, Instrument instrument, string baseCoin, CancellationToken cancellationToken)
    {
        IReadOnlyList<Bot> bots = [.. (await library.GetBotsAsync(cancellationToken)).Where(other => other.ExchangeAccountId == accountId)];

        // Only the spot holds a coin that several symbols trade; a position of linear and the shares of moex belong to their instrument.
        if (instrument.Category != MarketCategory.Spot)
        {
            return [.. bots.Where(other => other.Instrument == instrument)];
        }

        var coins = (await catalog.ListAsync(MarketCategory.Spot, cancellationToken)).ToDictionary(item => item.Symbol, item => item.BaseCoin);

        return [.. bots.Where(other => other.Instrument.Category == MarketCategory.Spot && coins.GetValueOrDefault(other.Instrument.Symbol) == baseCoin)];
    }

    /// <summary>The open trades of the account on the instrument, on the spot on any instrument of its coin.</summary>
    private async Task<IReadOnlyList<IdeaTrade>> TradeNeighboursAsync(Guid? accountId, Instrument instrument, string baseCoin, CancellationToken cancellationToken)
    {
        IReadOnlyList<IdeaTrade> open = [.. (await ideaTrades.GetOpenAsync(cancellationToken)).Where(trade => trade.AccountId == accountId)];

        if (instrument.Category != MarketCategory.Spot)
        {
            return [.. open.Where(trade => trade.Instrument == instrument)];
        }

        var coins = (await catalog.ListAsync(MarketCategory.Spot, cancellationToken)).ToDictionary(item => item.Symbol, item => item.BaseCoin);

        return [.. open.Where(trade => trade.Instrument.Category == MarketCategory.Spot && coins.GetValueOrDefault(trade.Instrument.Symbol) == baseCoin)];
    }
}