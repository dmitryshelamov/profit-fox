using System.Globalization;
using System.Threading.Channels;

using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.Bots;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.LiveTrading;
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>
///     Keeps one live bot for the take of its lease. Every input goes through one queue and one at a time, so the run of the
///     strategy needs no lock; every change is written in one unit of work with the lease checked first, and orders go to the
///     exchange only once they are written. Any gap, a restart, a reconnect or a resume rebuilds the run from the database.
/// </summary>
internal sealed class LiveBotSession(
    LeasedBot leased,
    ILiveBotRepository repository,
    IStrategyLibraryRepository library,
    IExchangeAccountRepository accounts,
    ISecretProtector protector,
    IExchangeLedger ledger,
    IOrderGateway gateway,
    IExchangeStreams streams,
    IMarketHistoryClient history,
    IInstrumentRulesProvider rules,
    IInstrumentCatalog catalog,
    ITransactionExecutor executor,
    ILiveTradingJournal journal,
    LiveBotLeases leases,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time)
{
    // A session that failed hands its bot back to any Worker after this pause rather than at the end of its lease: soon enough
    // to watch the bot again, late enough not to ask a failing exchange every second.
    private static readonly TimeSpan FailedTakePause = TimeSpan.FromSeconds(30);

    // The most closed candles one request of the exchange gives, which the indicators are warmed up with.
    private const int WarmUpCandles = 1000;

    // How many times the books are compared with the exchange before the bots of the account and the symbol stop.
    private const int ReconcileAttempts = 3;

    private readonly Channel<LiveInput> _inputs = Channel.CreateUnbounded<LiveInput>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<int, LiveOrder> _orders = [];

    // The ends the exchange told of orders whose fills have not all come yet.
    private readonly Guid _id = leased.Bot.BotId;
    private LiveBot _bot = leased.Bot;
    private LiveBotContext _context = null!;
    private StrategyRun _run = null!;
    private decimal? _lastClose;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The Worker renews the lease and reads the bot for all its sessions; a lost lease ends this one.
        using IDisposable held = leases.Hold(
            leased,
            bot => _inputs.Writer.TryWrite(new StatusInput(bot)),
            () =>
            {
                journal.Lost(_id);

                // Not in the loop of the Worker: what the token ends runs on its own.
                _ = lease.CancelAsync();
            });
        var subscriptions = new List<IAsyncDisposable>();
        journal.Took(_id, leased.Epoch);

        try
        {
            _context = await ContextAsync(lease.Token);
            subscriptions.Add(await streams.SubscribeCandlesAsync(
                _context.Bot.Instrument,
                _context.Bot.Interval,
                candle => _inputs.Writer.TryWrite(new CandleInput(candle)),
                () => _inputs.Writer.TryWrite(new RestoreInput()),
                lease.Token));
            subscriptions.Add(await streams.SubscribeAccountAsync(
                _context.Key,
                order =>
                {
                    if (Mine(order.ClientId))
                    {
                        _inputs.Writer.TryWrite(new OrderInput(order));
                    }
                },
                fill =>
                {
                    if (Mine(fill.ClientId))
                    {
                        _inputs.Writer.TryWrite(new FillInput(fill));
                    }
                },
                () => _inputs.Writer.TryWrite(new RestoreInput()),
                lease.Token));
            await RebuildAsync(fresh: _bot.StartPending, lease.Token);

            if (_bot.SessionFailures > 0 || _bot.SessionError is not null)
            {
                await WriteAsync(_ => Task.FromResult<IReadOnlyList<OrderCommand>>([]), bot => bot.SessionUp(), lease.Token);
            }

            while (!await AdvanceAsync(lease.Token))
            {
                LiveInput input = await _inputs.Reader.ReadAsync(lease.Token);

                try
                {
                    await HandleAsync(input, lease.Token);
                }
                catch (Exception exception) when (exception is not (LeaseLostException or OperationCanceledException))
                {
                    // The run may have taken what the database did not: build it again from the database.
                    await RebuildAsync(fresh: false, lease.Token);
                }
            }

            await repository.ReleaseAsync(_id, worker.Id, leased.Epoch, CancellationToken.None);
        }
        catch (LeaseLostException)
        {
            journal.Lost(_id);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or ExchangeUnavailableException))
        {
            // An exchange that does not answer for a while is no failure of the bot; anything else is counted, after the bot
            // was up too, and the third failure in a row fails a bot with nothing on the exchange.
            await SessionFailedAsync(exception.Message);
            await HandBackAsync(held);

            throw;
        }
        catch (ExchangeUnavailableException)
        {
            await HandBackAsync(held);

            throw;
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
            // The Worker stops or the lease is lost; the lease expires for any Worker.
        }
        finally
        {
            await lease.CancelAsync();

            foreach (IAsyncDisposable subscription in subscriptions)
            {
                await subscription.DisposeAsync();
            }
        }
    }

    /// <summary>Stops holding the bot and shortens the lease of this take to <see cref="FailedTakePause" />.</summary>
    private async Task HandBackAsync(IDisposable held)
    {
        held.Dispose();
        await repository.RenewAllAsync(worker.Id, [(_id, leased.Epoch)], time.GetUtcNow() + FailedTakePause, CancellationToken.None);
    }

    private Task SessionFailedAsync(string reason)
    {
        return executor.ExecuteAsync(
            async token =>
            {
                LiveBot bot = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();
                bool holdsOrders = (await repository.GetUnfinishedOrdersAsync(_id, token)).Count > 0;
                await repository.SaveAsync(bot.SessionFailed(reason, holdsOrders), time.GetUtcNow(), token);

                return 0;
            },
            CancellationToken.None);
    }

    private bool Mine(string clientId)
    {
        return ClientOrderId.TryParse(clientId, out ClientOrderId? id) && id.BotId == _id;
    }

    private async Task<LiveBotContext> ContextAsync(CancellationToken cancellationToken)
    {
        Bot bot = await library.GetBotAsync(_id, cancellationToken) ?? throw new LeaseLostException();
        StrategyVersion version = await library.GetVersionAsync(bot.StrategyId, _bot.StrategyVersion!.Value, cancellationToken)
                                  ?? throw new InvalidOperationException($"Bot {_id} trades a strategy version that is gone.");
        ExchangeAccount account = await accounts.GetAsync(bot.ExchangeAccountId!.Value, cancellationToken)
                                  ?? throw new InvalidOperationException($"Bot {_id} trades with an account that is gone.");
        ExchangeKey key = await LiveRequests.KeyAsync(accounts, protector, account, cancellationToken);
        InstrumentRules instrumentRules = await rules.GetRulesAsync(bot.Instrument, cancellationToken);
        FeeSchedule fees = await ledger.GetFeesAsync(key, bot.Instrument, cancellationToken);
        string baseCoin = (await catalog.ListAsync(bot.Instrument.Category, cancellationToken)).Single(item => item.Symbol == bot.Instrument.Symbol).BaseCoin;

        return new LiveBotContext(bot, version.Snapshot, new TradingConditions(bot.Balance, instrumentRules, fees), key, baseCoin);
    }

    /// <summary>
    ///     Builds the run again from the database, warms the indicators up, reads what the exchange did meanwhile and compares the
    ///     books with the exchange. A fresh run (a start, a resume) takes the candles before it only as history; a restored one gives
    ///     the strategy the last closed candle it has not seen.
    /// </summary>
    private async Task RebuildAsync(bool fresh, CancellationToken cancellationToken)
    {
        await executor.ExecuteAsync(
            async token =>
            {
                _bot = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();
                LivePosition? position = await repository.GetCurrentPositionAsync(_id, token);
                IReadOnlyList<LiveOrder> orders = await repository.GetUnfinishedOrdersAsync(_id, token);
                decimal pastPnl = await repository.GetPastPnlAsync(_id, token);
                _orders.Clear();

                foreach (LiveOrder order in orders)
                {
                    _orders[order.Number] = order;
                }

                _run = StrategyRun.Restore(_context.Strategy.Definition, _context.Strategy.Catalog(), _bot.ParameterValues, _context.Conditions, Snapshot(position, pastPnl));

                return 0;
            },
            cancellationToken);

        // The indicators are warm with every candle the bot saw before the strategy meets the fills it missed; a last candle it
        // has not seen comes after them.
        Candle? unseen = await WarmUpAsync(fresh, cancellationToken);
        await CatchUpAsync(cancellationToken);
        await ReconcileAsync(cancellationToken);

        if (unseen is not null)
        {
            await CandleAsync(unseen, cancellationToken);
        }

        if (_bot is { StartPending: true, Status: LiveBotStatus.Running })
        {
            await WriteAsync(token => Task.FromResult(_run.Handle(new RunStarted(time.GetUtcNow()))), bot => bot.Started(), cancellationToken);
        }
    }

    private RunSnapshot Snapshot(LivePosition? position, decimal pastPnl)
    {
        return new RunSnapshot(
            position?.Number ?? 1,
            position?.Quantity ?? 0m,
            position?.Invested ?? 0m,
            position?.Collateral ?? 0m,
            pastPnl,
            position?.RealizedPnl ?? 0m,
            position?.Fees ?? 0m,
            position?.LastEntryPrice,
            position?.LastEntryAt,
            position?.Closing ?? false,
            position?.SkippedOrders ?? 0,
            [
                .. _orders.Values.Select(order => new RunOrder(
                    RunId(order.Number),
                    order.NodePath,
                    order.Side,
                    order.Price,
                    order.Type == OrderType.Limit ? order.Quantity - order.FilledQuantity : null,
                    order.Status is LiveOrderStatus.PendingCancel or LiveOrderStatus.CancelSent,
                    order.Reserve,
                    order.FilledQuantity > 0m))
            ],
            _bot.LastOrderNumber);
    }

    /// <summary>The fills and the ends of the orders of the bot that the exchange reported while nobody listened.</summary>
    private async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        foreach (LiveOrder order in _orders.Values.ToArray())
        {
            await RefreshAsync(order.Number, cancellationToken);
        }
    }

    /// <summary>
    ///     Applies what the exchange knows of one order: its totals, then its trades as detail. The trades may lag behind the totals
    ///     on the exchange; the books do not wait for them.
    /// </summary>
    private async Task RefreshAsync(int number, CancellationToken cancellationToken)
    {
        if (!_orders.TryGetValue(number, out LiveOrder? order))
        {
            return;
        }

        string code = order.ClientId.Code;
        ExchangeOrder? known = await ledger.FindOrderAsync(_context.Key, _context.Bot.Instrument, code, cancellationToken);

        if (known is null)
        {
            // A cancel of an order the exchange never took ends the order.
            if (order.Status == LiveOrderStatus.CancelSent || (order.Status == LiveOrderStatus.PendingCancel && order.Attempts == 0 && order.ExchangeOrderId is null))
            {
                await EndAsync(order.Number, live => live.Cancelled(), cancellationToken);
            }

            return;
        }

        await OrderAsync(known, cancellationToken);

        foreach (ExchangeFill trade in await ledger.GetFillsAsync(_context.Key, _context.Bot.Instrument, code, cancellationToken))
        {
            await TradeAsync(trade, cancellationToken);
        }
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<Guid> bots, IReadOnlyList<string> differences) = await CompareAsync(cancellationToken);

        // A fill may land between the catch-up and the read of the exchange, and a neighbour bot may write its fill a moment after
        // the exchange shows it: the books are read again right away and once more after a pause before any bot stops.
        for (var attempt = 1; differences.Count > 0 && attempt < ReconcileAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(settings.PollInterval * 3, time, cancellationToken);
            }

            await CatchUpAsync(cancellationToken);
            (bots, differences) = await CompareAsync(cancellationToken);
        }

        if (differences.Count > 0)
        {
            await FailAllAsync(bots, string.Join(" ", differences), cancellationToken);
        }
    }

    /// <summary>The bots the books of this bot are compared with, and what differs.</summary>
    private async Task<(IReadOnlyList<Guid> Bots, IReadOnlyList<string> Differences)> CompareAsync(CancellationToken cancellationToken)
    {
        Bot bot = _context.Bot;
        IReadOnlyList<Bot> neighbours = await NeighboursAsync(cancellationToken);
        var holdings = new List<BotHolding>();
        var pending = new HashSet<ClientOrderId>();

        foreach (Bot neighbour in neighbours)
        {
            LiveBot live = await repository.GetAsync(neighbour.Id, cancellationToken);

            if (live.StrategyVersion is not { } number)
            {
                continue;
            }

            StrategyDirection direction = neighbour.Id == _id
                ? _context.Direction
                : (await library.GetVersionAsync(neighbour.StrategyId, number, cancellationToken))!.Snapshot.Definition.Direction;
            LivePosition? position = (await repository.GetPositionsAsync(neighbour.Id, cancellationToken)).FirstOrDefault();
            LiveOrder[] unfinished = [.. (await repository.GetOrdersAsync(neighbour.Id, cancellationToken)).Where(Unfinished)];
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
            .. (await ledger.GetOpenOrdersAsync(_context.Key, bot.Instrument, cancellationToken))
                .Select(order => ClientOrderId.TryParse(order.ClientId, out ClientOrderId? id) ? id : null)
                .OfType<ClientOrderId>()
                .Where(id => ids.Contains(id.BotId) && !pending.Contains(id))
        ];
        var finished = new List<ClientOrderId>();

        foreach (BotHolding holding in holdings)
        {
            foreach (ClientOrderId id in holding.ActiveOrders.Select(number => new ClientOrderId(holding.BotId, number)).Where(id => !open.Contains(id)))
            {
                if (await ledger.FindOrderAsync(_context.Key, bot.Instrument, id.Code, cancellationToken) is { State: not ExchangeOrderState.Open })
                {
                    finished.Add(id);
                }
            }
        }

        decimal held = await ledger.GetHoldingAsync(_context.Key, bot.Instrument, _context.BaseCoin, cancellationToken);
        IReadOnlyList<string> differences = Reconciliation.Check(bot.Instrument.Category, _context.Conditions.Rules.QuantityStep, holdings, new ExchangeHoldings(held, open, finished));

        return ([.. holdings.Select(holding => holding.BotId)], differences);
    }

    /// <summary>The bots of the account on the instrument of this bot, on the spot on any instrument of its coin.</summary>
    private async Task<IReadOnlyList<Bot>> NeighboursAsync(CancellationToken cancellationToken)
    {
        Bot bot = _context.Bot;
        IReadOnlyList<Bot> bots = [.. (await library.GetBotsAsync(cancellationToken)).Where(other => other.ExchangeAccountId == bot.ExchangeAccountId)];

        if (bot.Instrument.Category == MarketCategory.Linear)
        {
            return [.. bots.Where(other => other.Instrument == bot.Instrument)];
        }

        var coins = (await catalog.ListAsync(MarketCategory.Spot, cancellationToken)).ToDictionary(item => item.Symbol, item => item.BaseCoin);

        return [.. bots.Where(other => other.Instrument.Category == MarketCategory.Spot && coins.GetValueOrDefault(other.Instrument.Symbol) == _context.BaseCoin)];
    }

    /// <summary>Stops every working bot of the difference with its reason; a fix is never guessed.</summary>
    private async Task FailAllAsync(IEnumerable<Guid> bots, string reason, CancellationToken cancellationToken)
    {
        journal.Failed(_id, reason);
        await executor.ExecuteAsync(
            async token =>
            {
                foreach (Guid id in bots)
                {
                    LiveBot bot = id == _id
                        ? await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException()
                        : (await repository.LockAsync(id, time.GetUtcNow(), token))!;

                    if (bot.Status is LiveBotStatus.Running or LiveBotStatus.Paused)
                    {
                        bot = bot.Fail(reason);
                        await repository.SaveAsync(bot, time.GetUtcNow(), token);
                    }

                    if (id == _id)
                    {
                        _bot = bot;
                    }
                }

                return 0;
            },
            cancellationToken);
    }

    /// <summary>Gives the run the closed candles of the history but the last one, which is returned.</summary>
    /// <summary>
    ///     Warms the indicators up with the closed candles of the history; only a last candle a restored running bot has not seen
    ///     is held back for the strategy, and returned.
    /// </summary>
    private async Task<Candle?> WarmUpAsync(bool fresh, CancellationToken cancellationToken)
    {
        Bot bot = _context.Bot;
        DateTimeOffset now = time.GetUtcNow();
        // A Monday at midnight opens a candle of every interval; a month is taken at its longest.
        DateTimeOffset aligned = DateTimeOffset.UnixEpoch.AddDays(4);
        TimeSpan length = bot.Interval.CandleEnd(aligned) - aligned;
        var candles = new List<Candle>();

        await foreach (IReadOnlyList<Candle> page in history.GetCandlePagesAsync(bot.Instrument, bot.Interval, now - length * WarmUpCandles, now, cancellationToken))
        {
            candles.AddRange(page.Where(candle => candle.IsClosedAt(bot.Interval, now)));
        }

        Candle[] closed = [.. candles.DistinctBy(candle => candle.OpenTime).OrderBy(candle => candle.OpenTime).TakeLast(WarmUpCandles)];

        if (closed.Length == 0)
        {
            return null;
        }

        Candle last = closed[^1];
        bool unseen = !fresh && _bot.Status == LiveBotStatus.Running && last.OpenTime > (_bot.LastBarAt ?? DateTimeOffset.MinValue);

        foreach (Candle candle in unseen ? closed[..^1] : closed)
        {
            _run.WarmUp(Bar(candle));
        }

        _lastClose = last.Close.Value;

        // The history may lag behind the stream: the bot never goes back to a candle before the last it saw.
        if (!unseen && !(_bot.LastBarAt >= last.OpenTime))
        {
            await WriteAsync(_ => Task.FromResult<IReadOnlyList<OrderCommand>>([]), live => live with { LastBarAt = last.OpenTime }, cancellationToken);
        }

        return unseen ? last : null;
    }

    private BarClosed Bar(Candle candle)
    {
        return new BarClosed(
            _context.Bot.Interval.CandleEnd(candle.OpenTime),
            new Bar(candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value));
    }

    private async Task HandleAsync(LiveInput input, CancellationToken cancellationToken)
    {
        switch (input)
        {
            case CandleInput { Candle: var candle }:
                _lastClose = candle.Close.Value;

                if (_bot.Status == LiveBotStatus.Running && candle.OpenTime > (_bot.LastBarAt ?? DateTimeOffset.MinValue))
                {
                    await CandleAsync(candle, cancellationToken);
                }

                break;
            case FillInput { Fill: var trade }:
                await TradeAsync(trade, cancellationToken);

                break;
            case OrderInput { Order: var order }:
                await OrderAsync(order, cancellationToken);

                break;
            case RestoreInput:
                await RebuildAsync(fresh: false, cancellationToken);

                break;
            case StatusInput { Bot: var bot }:
                bool resumed = _bot.Status == LiveBotStatus.Paused && bot.Status == LiveBotStatus.Running;

                // A renewal may read the row before the last write of this session: what the session wrote stays.
                _bot = bot with
                {
                    LastOrderNumber = Math.Max(bot.LastOrderNumber, _bot.LastOrderNumber),
                    LastBarAt = bot.LastBarAt > _bot.LastBarAt ? bot.LastBarAt : _bot.LastBarAt
                };

                if (resumed)
                {
                    await RebuildAsync(fresh: true, cancellationToken);
                }

                break;
        }

        await SendAsync(cancellationToken);
    }

    /// <summary>
    ///     A closed candle reaches the strategy once and only while the bot runs, both by the bot as written: one it has seen is
    ///     passed over, and one that meets a pause or a stop the session has not heard of yet is not traded.
    /// </summary>
    private Task CandleAsync(Candle candle, CancellationToken cancellationToken)
    {
        return WriteAsync(
            _ =>
            {
                bool seen = _bot.LastBarAt >= candle.OpenTime;

                return Task.FromResult(!seen && _bot.Status == LiveBotStatus.Running ? _run.Handle(Bar(candle)) : []);
            },
            bot => bot.LastBarAt >= candle.OpenTime ? bot : bot with { LastBarAt = candle.OpenTime },
            cancellationToken);
    }

    /// <summary>
    ///     A trade of the exchange is kept with its order once, by the exchange's id of it, as detail only: the totals of the order
    ///     move the position. A trade that comes after the end of its order is kept the same.
    /// </summary>
    private async Task TradeAsync(ExchangeFill trade, CancellationToken cancellationToken)
    {
        // Every order the bot ever placed is written, numbered up to the last number of the bot.
        if (!ClientOrderId.TryParse(trade.ClientId, out ClientOrderId? id) || id.Number > _bot.LastOrderNumber)
        {
            return;
        }

        await WriteAsync(
            async token =>
            {
                await repository.AddFillAsync(
                    new LiveFill(trade.ExecId, _id, id.Number, trade.Quantity, trade.Price, trade.Fee, Asset(trade.FeeCoin), trade.At),
                    token);

                return [];
            },
            bot => bot,
            cancellationToken);
    }

    private FeeAsset Asset(string coin)
    {
        return coin == _context.BaseCoin ? FeeAsset.Base : FeeAsset.Quote;
    }

    /// <summary>
    ///     What the exchange says of an order, from its stream or its REST: what its totals grew by since the last update is a fill of
    ///     the position, a repeat grows them by nothing; then its end or that it was taken. The end of an order comes with its last
    ///     totals, so an order is done with nothing of it left out.
    /// </summary>
    private async Task OrderAsync(ExchangeOrder order, CancellationToken cancellationToken)
    {
        if (!ClientOrderId.TryParse(order.ClientId, out ClientOrderId? id) || !_orders.TryGetValue(id.Number, out LiveOrder? live))
        {
            return;
        }

        decimal grown = order.FilledQuantity - live.FilledQuantity;

        if (grown <= 0m && order.State == ExchangeOrderState.Open && live.Status != LiveOrderStatus.PendingSubmit)
        {
            return;
        }

        if (order.State == ExchangeOrderState.Rejected)
        {
            journal.Refused(_id, live.ClientId.Code, order.RejectReason ?? "rejected");
        }

        await WriteAsync(
            async token =>
            {
                var commands = new List<OrderCommand>();
                LiveOrder next = live.Taken(order.ExchangeId);

                if (grown > 0m)
                {
                    decimal value = order.FilledValue - live.FilledValue;
                    decimal fee = order.Fee - live.Fee;
                    bool filled = order.State == ExchangeOrderState.Filled;
                    next = next.Grown(order.FilledQuantity, order.FilledValue, order.Fee, filled);
                    await SaveOrderAsync(next, token);
                    decimal price = value > 0m ? value / grown : live.Price ?? _lastClose ?? 0m;
                    commands.AddRange(Apply(new OrderFilled(time.GetUtcNow(), RunId(live.Number), grown, price, filled, fee, Asset(order.FeeCoin))));

                    if (filled)
                    {
                        return commands;
                    }
                }

                switch (order.State)
                {
                    case ExchangeOrderState.Filled:
                    case ExchangeOrderState.Cancelled:
                        // An order done with no growth left: the run keeps what it got, the order keeps how it ended.
                        await SaveOrderAsync(order.State == ExchangeOrderState.Filled ? next.Completed() : next.Cancelled(), token);
                        commands.AddRange(Apply(new OrderCancelled(time.GetUtcNow(), RunId(live.Number))));

                        break;
                    case ExchangeOrderState.Rejected:
                        LiveOrder rejected = next.Rejected(order.RejectReason ?? "Bybit rejected the order.");
                        await SaveOrderAsync(rejected, token);
                        _bot = _bot.Fail(rejected.RejectReason!);
                        commands.AddRange(Apply(new OrderCancelled(time.GetUtcNow(), RunId(live.Number))));

                        break;
                    default:
                        await SaveOrderAsync(next, token);

                        break;
                }

                return commands;
            },
            bot => bot,
            cancellationToken);
    }

    /// <summary>
    ///     Ends an order without a fill to come; a refusal stops the bot with its reason first, so the strategy does not place the
    ///     order again.
    /// </summary>
    private Task EndAsync(int number, Func<LiveOrder, LiveOrder> end, CancellationToken cancellationToken)
    {
        return WriteAsync(
            async token =>
            {
                LiveOrder ended = end(_orders[number]);
                await SaveOrderAsync(ended, token);

                if (ended.RejectReason is { } reason)
                {
                    _bot = _bot.Fail(reason);
                }

                return Apply(new OrderCancelled(time.GetUtcNow(), RunId(number)));
            },
            bot => bot,
            cancellationToken);
    }

    /// <summary>The strategy reacts while the bot runs; otherwise only the books are kept.</summary>
    private IReadOnlyList<OrderCommand> Apply(StrategyEvent @event)
    {
        return _bot.Status == LiveBotStatus.Running ? _run.Handle(@event) : _run.Record(@event);
    }

    /// <summary>
    ///     One unit of work: the lease is checked first, then <paramref name="change" /> writes and runs the strategy, its commands
    ///     become orders and cancels, and the positions and the bot are written.
    /// </summary>
    private async Task WriteAsync(Func<CancellationToken, Task<IReadOnlyList<OrderCommand>>> change, Func<LiveBot, LiveBot> bot, CancellationToken cancellationToken)
    {
        await executor.ExecuteAsync(
            async token =>
            {
                LiveBot locked = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();
                _bot = locked;
                LivePosition? before = await repository.GetCurrentPositionAsync(_id, token);
                await SavePositionsAsync(before, token);
                IReadOnlyList<OrderCommand> commands = await change(token);
                await SavePositionsAsync(before, token);
                await CommandsAsync(commands, token);
                _bot = bot(_bot) with { LastOrderNumber = _run.Snapshot().LastOrderNumber };
                await repository.SaveAsync(_bot, time.GetUtcNow(), token);

                return 0;
            },
            cancellationToken);
    }

    /// <summary>The current position of the run, and the one it closed with this change.</summary>
    private async Task SavePositionsAsync(LivePosition? before, CancellationToken cancellationToken)
    {
        RunSnapshot snapshot = _run.Snapshot();
        RunState state = _run.State;

        if (state.Closed is { } closed && before is { ClosedAt: null } && before.Number == closed.Number)
        {
            await repository.SavePositionAsync(
                before with { Open = false, Closing = false, Quantity = 0m, Invested = 0m, Collateral = 0m, RealizedPnl = closed.RealizedPnl, Fees = closed.Fees, ClosedAt = closed.At },
                cancellationToken);
        }

        await repository.SavePositionAsync(
            new LivePosition(
                _id,
                snapshot.Position,
                state.Status == PositionStatus.Open,
                snapshot.Closing,
                snapshot.Quantity,
                snapshot.Invested,
                snapshot.Collateral,
                snapshot.RealizedPnl,
                snapshot.Fees,
                snapshot.LastEntryPrice,
                snapshot.LastEntryAt,
                snapshot.SkippedOrders,
                null),
            cancellationToken);
    }

    private async Task CommandsAsync(IReadOnlyList<OrderCommand> commands, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        RunSnapshot snapshot = _run.Snapshot();

        foreach (OrderCommand command in commands)
        {
            switch (command)
            {
                case PlaceOrder place:
                    RunOrder? active = snapshot.Orders.SingleOrDefault(order => order.Id == place.OrderId);
                    var order = LiveOrder.New(
                        _id,
                        Number(place.OrderId),
                        snapshot.Position,
                        place.NodePath,
                        place.Side,
                        place.Type,
                        place.Price,
                        place.Quantity ?? QuantityOf(place.Amount),
                        place.Amount,
                        active?.Reserve ?? 0m,
                        now);
                    await SaveOrderAsync(order, cancellationToken);

                    break;
                case CancelOrder cancel when _orders.TryGetValue(Number(cancel.OrderId), out LiveOrder? target):
                    await SaveOrderAsync(target.RequestCancel(now), cancellationToken);

                    break;
            }
        }
    }

    /// <summary>An order by amount goes to the exchange by quantity at the last close, so its fills tell when it is complete.</summary>
    private decimal? QuantityOf(decimal? amount)
    {
        if (amount is not { } value || _lastClose is not { } close || close <= 0m)
        {
            return null;
        }

        decimal quantity = value / close;

        return quantity - quantity % _context.Conditions.Rules.QuantityStep;
    }

    private async Task SaveOrderAsync(LiveOrder order, CancellationToken cancellationToken)
    {
        await repository.SaveOrderAsync(order, cancellationToken);

        if (Unfinished(order))
        {
            _orders[order.Number] = order;
        }
        else
        {
            _orders.Remove(order.Number);
        }
    }

    private static bool Unfinished(LiveOrder order)
    {
        return order.Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.Active or LiveOrderStatus.PendingCancel or LiveOrderStatus.CancelSent;
    }

    /// <summary>Sends the saved orders and cancels that are due, by number, outside any unit of work.</summary>
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        LiveOrder[] due =
        [
            .. _orders.Values
                .Where(order => order.Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.PendingCancel && (order.NextAttemptAt is null || order.NextAttemptAt <= now))
                .OrderBy(order => order.Number)
        ];

        foreach (LiveOrder order in due)
        {
            executor.EnsureNoTransaction();
            string code = order.ClientId.Code;
            OrderOutcome outcome = order.Status == LiveOrderStatus.PendingSubmit
                ? await gateway.PlaceAsync(_context.Key, _context.Bot.Instrument, new OrderRequest(code, order.Side, order.Type, order.Price, order.Quantity, order.Quantity is null ? order.Amount : null), cancellationToken)
                : await gateway.CancelAsync(_context.Key, _context.Bot.Instrument, code, cancellationToken);

            switch (outcome.Kind)
            {
                case OrderOutcomeKind.Accepted when order.Status == LiveOrderStatus.PendingSubmit:
                    journal.Sent(_id, code);
                    await WriteOrderAsync(order.Taken(outcome.ExchangeId), cancellationToken);

                    break;
                case OrderOutcomeKind.Accepted:
                    await WriteOrderAsync(order.CancelTaken(), cancellationToken);

                    break;
                case OrderOutcomeKind.AlreadyDone:
                    await WriteOrderAsync(order.CancelTaken(), cancellationToken);
                    await RefreshAsync(order.Number, cancellationToken);

                    break;
                case OrderOutcomeKind.Transient:
                    LiveOrder deferred = order.Deferred(now);
                    await WriteOrderAsync(deferred, cancellationToken);
                    _ = LaterAsync(new WakeInput(), deferred.NextAttemptAt!.Value - now, cancellationToken);

                    break;
                case OrderOutcomeKind.Permanent:
                    journal.Refused(_id, code, outcome.Reason ?? "refused");

                    if (order.Status == LiveOrderStatus.PendingSubmit)
                    {
                        await EndAsync(order.Number, live => live.Rejected(outcome.Reason ?? "Bybit refused the order."), cancellationToken);
                    }
                    else
                    {
                        // The order stays on the exchange and is never left unwatched: the bot stops with the reason and asks
                        // again ever more slowly, until Bybit takes the cancel or the owner cancels the order on Bybit.
                        LiveOrder paused = order.Deferred(now);
                        await WriteAsync(
                            async token =>
                            {
                                await SaveOrderAsync(paused with { UpdatedAt = time.GetUtcNow() }, token);

                                return [];
                            },
                            bot => bot.Fail($"Bybit refused to cancel order {code}: {outcome.Reason} Cancel it on Bybit: the bot stops once Bybit shows the order done."),
                            cancellationToken);
                        _ = LaterAsync(new WakeInput(), paused.NextAttemptAt!.Value - now, cancellationToken);
                    }

                    break;
            }
        }
    }

    private Task WriteOrderAsync(LiveOrder order, CancellationToken cancellationToken)
    {
        return WriteAsync(
            async token =>
            {
                await SaveOrderAsync(order with { UpdatedAt = time.GetUtcNow() }, token);

                return [];
            },
            bot => bot,
            cancellationToken);
    }

    private async Task LaterAsync(LiveInput input, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, time, cancellationToken);
            _inputs.Writer.TryWrite(input);
        }
        catch (OperationCanceledException)
        {
            // The session ended.
        }
    }

    /// <summary>
    ///     Moves the bot on by its status: a stopping bot cancels its orders and stops once none is left, a stopped bot closes
    ///     its position on request. Returns whether the session is done with the bot.
    /// </summary>
    private async Task<bool> AdvanceAsync(CancellationToken cancellationToken)
    {
        switch (_bot.Status)
        {
            case LiveBotStatus.Stopping:
                LiveOrder[] cancel = [.. _orders.Values.Where(order => order.Status is LiveOrderStatus.PendingSubmit or LiveOrderStatus.Active)];

                if (cancel.Length > 0)
                {
                    await WriteAsync(
                        async token =>
                        {
                            foreach (LiveOrder order in cancel)
                            {
                                await SaveOrderAsync(order.RequestCancel(time.GetUtcNow()), token);
                            }

                            return [];
                        },
                        bot => bot,
                        cancellationToken);
                    await SendAsync(cancellationToken);

                    return false;
                }

                if (_orders.Count == 0)
                {
                    await WriteAsync(_ => Task.FromResult<IReadOnlyList<OrderCommand>>([]), bot => bot.Stopped(), cancellationToken);
                }

                return false;
            case LiveBotStatus.Stopped or LiveBotStatus.Failed when _bot.CloseRequested:
                if (_run.State.Status is PositionStatus.Open && _orders.Count == 0)
                {
                    await WriteAsync(token => Task.FromResult(_run.Close(time.GetUtcNow())), bot => bot, cancellationToken);
                    await SendAsync(cancellationToken);

                    return false;
                }

                if (_run.State.Status is not (PositionStatus.Open or PositionStatus.Closing) && _orders.Count == 0)
                {
                    await WriteAsync(_ => Task.FromResult<IReadOnlyList<OrderCommand>>([]), bot => bot.Closed(), cancellationToken);

                    return true;
                }

                return false;
            case LiveBotStatus.Stopped or LiveBotStatus.Failed:
                return true;
            default:
                return false;
        }
    }

    private static string RunId(int number)
    {
        return string.Create(CultureInfo.InvariantCulture, $"o-{number}");
    }

    private static int Number(string runId)
    {
        return int.Parse(runId.AsSpan(2), CultureInfo.InvariantCulture);
    }
}