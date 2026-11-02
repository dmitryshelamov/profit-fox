using System.Globalization;
using System.Threading.Channels;

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
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time)
{
    // The most closed candles one request of the exchange gives, which the indicators are warmed up with.
    private const int WarmUpCandles = 1000;

    private readonly Channel<LiveInput> _inputs = Channel.CreateUnbounded<LiveInput>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<int, LiveOrder> _orders = [];

    // The ends the exchange told of orders whose fills have not all come yet.
    private readonly Dictionary<int, ExchangeOrder> _ends = [];
    private readonly Guid _id = leased.Bot.BotId;
    private LiveBot _bot = leased.Bot;
    private LiveBotContext _context = null!;
    private StrategyRun _run = null!;
    private decimal? _lastClose;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task keeper = KeepAsync(lease);
        var subscriptions = new List<IAsyncDisposable>();
        journal.Took(_id, leased.Epoch);
        var up = false;

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
            up = true;

            if (_bot.SessionFailures > 0)
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
        catch (Exception exception) when (!up && exception is not OperationCanceledException)
        {
            // The bot is counted as failed to take up; the third failure in a row fails it, so no Worker keeps trying.
            await SessionFailedAsync(exception.Message);

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

            await keeper;
        }
    }

    private Task SessionFailedAsync(string reason)
    {
        return executor.ExecuteAsync(
            async token =>
            {
                LiveBot bot = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();
                await repository.SaveAsync(bot.SessionFailed(reason), time.GetUtcNow(), token);

                return 0;
            },
            CancellationToken.None);
    }

    /// <summary>Renews the lease on every poll and hands the bot as the database holds it to the session; a lost lease ends it.</summary>
    private async Task KeepAsync(CancellationTokenSource lease)
    {
        TimeSpan interval = settings.PollInterval < settings.LeaseRenewal ? settings.PollInterval : settings.LeaseRenewal;
        DateTimeOffset leasedUntil = time.GetUtcNow() + settings.LeaseDuration;

        try
        {
            while (true)
            {
                await Task.Delay(interval, time, lease.Token);
                DateTimeOffset until = time.GetUtcNow() + settings.LeaseDuration;
                LiveBot? bot;

                try
                {
                    bot = await repository.RenewAsync(_id, worker.Id, leased.Epoch, until, lease.Token);
                }
                catch (Exception) when (!lease.IsCancellationRequested && time.GetUtcNow() + interval < leasedUntil)
                {
                    continue;
                }

                if (bot is null)
                {
                    journal.Lost(_id);
                    await lease.CancelAsync();

                    return;
                }

                leasedUntil = until;
                _inputs.Writer.TryWrite(new StatusInput(bot));
            }
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
            // The session ended.
        }
        catch (Exception)
        {
            await lease.CancelAsync();
        }
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

        // The indicators are warm before the strategy meets the fills it missed; the last candle comes after them.
        Candle? last = await WarmUpAsync(cancellationToken);
        await CatchUpAsync(cancellationToken);
        await ReconcileAsync(cancellationToken);

        if (last is not null)
        {
            await LastCandleAsync(last, fresh, cancellationToken);
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

    /// <summary>Applies what the exchange knows of one order: its fills, then its end or that it was taken.</summary>
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
                await EndAsync(order.Number, LiveOrderStatus.Cancelled, null, cancellationToken);
            }

            return;
        }

        foreach (ExchangeFill fill in await ledger.GetFillsAsync(_context.Key, _context.Bot.Instrument, code, cancellationToken))
        {
            await FillAsync(fill, cancellationToken);
        }

        await StateAsync(known, cancellationToken);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<Guid> bots, IReadOnlyList<string> differences) = await CompareAsync(cancellationToken);

        if (differences.Count > 0)
        {
            // A fill may land between the catch-up and the read of the exchange: the books are read again before any bot stops.
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
    private async Task<Candle?> WarmUpAsync(CancellationToken cancellationToken)
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

        foreach (Candle candle in closed[..^1])
        {
            _run.WarmUp(Bar(candle));
        }

        _lastClose = closed[^1].Close.Value;

        return closed[^1];
    }

    /// <summary>The last closed candle reaches a restored running strategy if it has not seen it; otherwise it is history.</summary>
    private async Task LastCandleAsync(Candle last, bool fresh, CancellationToken cancellationToken)
    {
        if (!fresh && _bot.Status == LiveBotStatus.Running && last.OpenTime > (_bot.LastBarAt ?? DateTimeOffset.MinValue))
        {
            await CandleAsync(last, cancellationToken);
        }
        else
        {
            _run.WarmUp(Bar(last));

            if (_bot.LastBarAt != last.OpenTime)
            {
                await WriteAsync(_ => Task.FromResult<IReadOnlyList<OrderCommand>>([]), live => live with { LastBarAt = last.OpenTime }, cancellationToken);
            }
        }
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
            case FillInput { Fill: var fill }:
                await FillAsync(fill, cancellationToken);

                break;
            case OrderInput { Order: var order } when ClientOrderId.TryParse(order.ClientId, out ClientOrderId? id):
                // The end of an order may come before its last fills: they are read first.
                if (order.State != ExchangeOrderState.Open)
                {
                    await RefreshAsync(id.Number, cancellationToken);
                }
                else
                {
                    await StateAsync(order, cancellationToken);
                }

                break;
            case RestoreInput:
                await RebuildAsync(fresh: false, cancellationToken);

                break;
            case RefreshInput { Number: var number }:
                await RefreshAsync(number, cancellationToken);

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

    /// <summary>A fill is written once, by the exchange's id of it; a fill of an order that is done or unknown is not the run's.</summary>
    private async Task FillAsync(ExchangeFill fill, CancellationToken cancellationToken)
    {
        if (!ClientOrderId.TryParse(fill.ClientId, out ClientOrderId? id) || !_orders.TryGetValue(id.Number, out LiveOrder? order))
        {
            return;
        }

        await WriteAsync(
            async token =>
            {
                var live = new LiveFill(fill.ExecId, _id, order.Number, fill.Quantity, fill.Price, fill.Fee, Asset(fill.FeeCoin), fill.At);

                if (!await repository.AddFillAsync(live, token))
                {
                    return [];
                }

                decimal filled = order.FilledQuantity + fill.Quantity;
                bool complete = order.Quantity is { } quantity && filled >= quantity;
                await SaveOrderAsync(order with { FilledQuantity = filled, Status = complete ? LiveOrderStatus.Filled : order.Status }, token);

                return Apply(new OrderFilled(fill.At, RunId(order.Number), fill.Quantity, fill.Price, complete, fill.Fee, Asset(fill.FeeCoin)));
            },
            bot => bot,
            cancellationToken);

        if (_ends.TryGetValue(id.Number, out ExchangeOrder? end))
        {
            await StateAsync(end, cancellationToken);
        }
    }

    private FeeAsset Asset(string coin)
    {
        return coin == _context.BaseCoin ? FeeAsset.Base : FeeAsset.Quote;
    }

    /// <summary>What the exchange says of an order: taken, or ended by a cancel or a refusal.</summary>
    private async Task StateAsync(ExchangeOrder order, CancellationToken cancellationToken)
    {
        if (!ClientOrderId.TryParse(order.ClientId, out ClientOrderId? id) || !_orders.TryGetValue(id.Number, out LiveOrder? live))
        {
            return;
        }

        switch (order.State)
        {
            case ExchangeOrderState.Open when live.Status == LiveOrderStatus.PendingSubmit:
                await WriteAsync(
                    async token =>
                    {
                        await SaveOrderAsync(live with { Status = LiveOrderStatus.Active, ExchangeOrderId = order.ExchangeId }, token);

                        return [];
                    },
                    bot => bot,
                    cancellationToken);

                break;
            case ExchangeOrderState.Cancelled:
            case ExchangeOrderState.Filled when live.Status != LiveOrderStatus.Filled:
                if (live.FilledQuantity < order.FilledQuantity)
                {
                    // Bybit tells the end on its own topic, often before the last fills: the order waits for what the exchange counted.
                    _ends[live.Number] = order;
                    _ = LaterAsync(new RefreshInput(live.Number), settings.PollInterval, cancellationToken);

                    break;
                }

                // The run keeps only what the order got; an order by amount is done once the exchange says it filled.
                await EndAsync(live.Number, order.State == ExchangeOrderState.Filled ? LiveOrderStatus.Filled : LiveOrderStatus.Cancelled, null, cancellationToken);

                break;
            case ExchangeOrderState.Rejected:
                journal.Refused(_id, live.ClientId.Code, order.RejectReason ?? "rejected");
                await EndAsync(live.Number, LiveOrderStatus.Rejected, order.RejectReason ?? "Bybit rejected the order.", cancellationToken);

                break;
        }
    }

    /// <summary>
    ///     Ends an order without a fill to come; a refusal stops the bot with its reason first, so the strategy does not place the
    ///     order again.
    /// </summary>
    private Task EndAsync(int number, LiveOrderStatus status, string? reason, CancellationToken cancellationToken)
    {
        return WriteAsync(
            async token =>
            {
                LiveOrder order = _orders[number];
                await SaveOrderAsync(order with { Status = status, RejectReason = reason }, token);

                if (reason is not null)
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
                    var order = new LiveOrder(
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
                        LiveOrderStatus.PendingSubmit,
                        0m,
                        null,
                        null,
                        0,
                        null,
                        now,
                        now);
                    await SaveOrderAsync(order, cancellationToken);

                    break;
                case CancelOrder cancel when _orders.TryGetValue(Number(cancel.OrderId), out LiveOrder? target):
                    await SaveOrderAsync(target with { Status = LiveOrderStatus.PendingCancel, NextAttemptAt = null, UpdatedAt = now }, cancellationToken);

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
            _ends.Remove(order.Number);
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
                    await WriteOrderAsync(order with { Status = LiveOrderStatus.Active, ExchangeOrderId = outcome.ExchangeId ?? order.ExchangeOrderId }, cancellationToken);

                    break;
                case OrderOutcomeKind.Accepted:
                    await WriteOrderAsync(order with { Status = LiveOrderStatus.CancelSent }, cancellationToken);

                    break;
                case OrderOutcomeKind.AlreadyDone:
                    await WriteOrderAsync(order with { Status = LiveOrderStatus.CancelSent }, cancellationToken);
                    await RefreshAsync(order.Number, cancellationToken);

                    break;
                case OrderOutcomeKind.Transient:
                    var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, order.Attempts), 60));
                    await WriteOrderAsync(order with { Attempts = order.Attempts + 1, NextAttemptAt = now + delay }, cancellationToken);
                    _ = LaterAsync(new WakeInput(), delay, cancellationToken);

                    break;
                case OrderOutcomeKind.Permanent:
                    journal.Refused(_id, code, outcome.Reason ?? "refused");

                    if (order.Status == LiveOrderStatus.PendingSubmit)
                    {
                        await EndAsync(order.Number, LiveOrderStatus.Rejected, outcome.Reason ?? "Bybit refused the order.", cancellationToken);
                    }
                    else
                    {
                        // The order stays on the exchange: the bot stops with the reason and asks again ever more slowly.
                        var pause = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, order.Attempts), 60));
                        await WriteAsync(
                            async token =>
                            {
                                await SaveOrderAsync(order with { Attempts = order.Attempts + 1, NextAttemptAt = now + pause, UpdatedAt = time.GetUtcNow() }, token);

                                return [];
                            },
                            bot => bot.Fail(outcome.Reason ?? "Bybit refused the cancel."),
                            cancellationToken);
                        _ = LaterAsync(new WakeInput(), pause, cancellationToken);
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
                                await SaveOrderAsync(order with { Status = LiveOrderStatus.PendingCancel, NextAttemptAt = null, UpdatedAt = time.GetUtcNow() }, token);
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