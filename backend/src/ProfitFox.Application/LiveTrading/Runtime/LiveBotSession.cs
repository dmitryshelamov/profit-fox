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
using ProfitFox.Domain.Services.Strategies.Execution;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;
using ProfitFox.Domain.ValueObjects.Strategies;

namespace ProfitFox.Application.LiveTrading.Runtime;

/// <summary>
///     Trades one live bot while this Worker holds its lease.
///     <list type="number">
///         <item><see cref="RunAsync" /> takes the lease, subscribes to candles and to the account and puts every event into one queue.</item>
///         <item><see cref="RebuildAsync" /> builds the run of the strategy from the database, warms up the indicators, applies what the exchange did meanwhile and reconciles the books with the exchange.</item>
///         <item>The loop takes the events one by one: <see cref="AdvanceAsync" /> moves the bot by its status, <see cref="HandleAsync" /> applies the event, <see cref="SendAsync" /> sends the orders and cancels that are due.</item>
///     </list>
///     Events are handled one at a time, so the run needs no lock. Every change goes through <c>WriteAsync</c>: one unit of
///     work that checks the lease first. Orders go to the exchange only after they are written.
/// </summary>
/// <remarks>
///     <c>_bot</c>, <c>_orders</c> and <c>_run</c> change inside a unit of work and run ahead of the database when it rolls back. Any
///     failure of an event is therefore followed by <see cref="RebuildAsync" />, and so is a gap in a stream or a resume.
/// </remarks>
internal sealed class LiveBotSession(
    LeasedBot leased,
    ILiveBotRepository repository,
    IIdeaTradeRepository ideaTrades,
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
    // How many times the books are compared with the exchange before the bots of the account and the symbol stop.
    private const int ReconcileAttempts = 3;

    private readonly Channel<LiveInput> _inputs = Channel.CreateUnbounded<LiveInput>(new UnboundedChannelOptions { SingleReader = true });
    private static readonly Task<IReadOnlyList<OrderCommand>> NoCommands = Task.FromResult<IReadOnlyList<OrderCommand>>([]);

    // The unfinished orders of the bot by number.
    private readonly Dictionary<int, LiveOrder> _orders = [];

    private readonly Guid _id = leased.Bot.BotId;
    private readonly LiveBotReconciler _reconciler = new LiveBotReconciler(repository, ideaTrades, library, ledger, catalog, executor, journal, worker, time);
    private readonly LiveWarmUpHistory _history = new LiveWarmUpHistory(history, time);
    private LiveBot _bot = leased.Bot;
    private LiveBotContext _context = null!;
    private StrategyRun _run = null!;
    private decimal? _lastClose;
    private bool _reconcileScheduled;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The Worker renews the lease and reads the bot for all its sessions; a lost lease ends this one.
        using IDisposable held = leases.Hold(
            leased,
            bot => _inputs.Writer.TryWrite(new StatusInput(bot)),
            () =>
            {
                // First, so the session ends whatever the journal does; not in the loop of the Worker, what the token ends runs
                // on its own.
                _ = lease.CancelAsync();
                journal.Lost(_id);
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
                    else if (!ClientOrderId.TryParse(fill.ClientId, out _) && !IdeaTradeOrderId.TryParse(fill.ClientId, out _) && fill.Instrument == _context.Bot.Instrument)
                    {
                        // An execution of the stop or the target of a trade has no client id: whose it is, the database tells.
                        _inputs.Writer.TryWrite(new ForeignFillInput(fill.ExchangeOrderId));
                    }
                },
                () => _inputs.Writer.TryWrite(new RestoreInput()),
                lease.Token));
            await RebuildAsync(fresh: _bot.StartPending, lease.Token);

            if (_bot.SessionFailures > 0 || _bot.SessionError is not null)
            {
                await WriteBotAsync(bot => bot.SessionUp(), lease.Token);
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
            await leases.HandBackAsync(_id, leased.Epoch);

            throw;
        }
        catch (Exception exception) when (exception is ExchangeUnavailableException || (exception is OperationCanceledException && !lease.IsCancellationRequested))
        {
            // An exchange that does not answer, or a cancellation that is not the session's own, such as a timeout of a client.
            await leases.HandBackAsync(_id, leased.Epoch);

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

                _run = StrategyRun.Restore(_context.Strategy.Definition, _context.Strategy.Catalog(), _bot.ParameterValues, _context.Conditions, LiveRunMapping.Snapshot(position, pastPnl, _orders.Values, _bot.LastOrderNumber));

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

    /// <summary>Compares the books with the exchange and stops the bots of a difference that stays after the exchange is read again.</summary>
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        (IReadOnlyList<Guid> bots, IReadOnlyList<Guid> trades, IReadOnlyList<string> differences) = await _reconciler.CompareAsync(_context, cancellationToken);

        // A fill may land between the catch-up and the read of the exchange, and a neighbour bot may write its fill a moment after
        // the exchange shows it: the books are read again right away and once more after a pause before any bot stops.
        for (var attempt = 1; differences.Count > 0 && attempt < ReconcileAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(settings.PollInterval * 3, time, cancellationToken);
            }

            await CatchUpAsync(cancellationToken);
            (bots, trades, differences) = await _reconciler.CompareAsync(_context, cancellationToken);
        }

        if (differences.Count > 0)
        {
            _bot = await _reconciler.FailAllAsync(leased, bots, trades, string.Join(" ", differences), cancellationToken) ?? _bot;
        }
    }

    /// <summary>
    ///     Warms the indicators up with the closed candles of the history; only a last candle a restored running bot has not seen
    ///     is held back for the strategy, and returned.
    /// </summary>
    private async Task<Candle?> WarmUpAsync(bool fresh, CancellationToken cancellationToken)
    {
        Candle[] closed = await _history.ClosedCandlesAsync(_context.Bot.Instrument, _context.Bot.Interval, cancellationToken);

        if (closed.Length == 0)
        {
            return null;
        }

        Candle last = closed[^1];
        bool unseen = !fresh && _bot.Awaits(last.OpenTime);

        foreach (Candle candle in unseen ? closed[..^1] : closed)
        {
            _run.WarmUp(Bar(candle));
        }

        _lastClose = last.Close.Value;

        // The history may lag behind the stream: the bot never goes back to a candle before the last it saw.
        if (!unseen && !(_bot.LastBarAt >= last.OpenTime))
        {
            await WriteBotAsync(live => live with { LastBarAt = last.OpenTime }, cancellationToken);
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

                if (_bot.Awaits(candle.OpenTime))
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
            case ForeignFillInput foreign when !_reconcileScheduled:
                if (foreign.ExchangeOrderId is { } exchangeId && await ideaTrades.IsChildAsync(exchangeId, cancellationToken))
                {
                    break;
                }

                // The REST reads may lag behind the stream: the comparison waits as long as the one repeated after a difference.
                _reconcileScheduled = true;
                _ = LaterAsync(new ReconcileInput(), settings.PollInterval * 3, cancellationToken);

                break;
            case ReconcileInput:
                _reconcileScheduled = false;

                if (_bot.Status is LiveBotStatus.Running or LiveBotStatus.Paused)
                {
                    await ReconcileAsync(cancellationToken);
                }

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
            _ => _bot.Awaits(candle.OpenTime) ? Task.FromResult(_run.Handle(Bar(candle))) : NoCommands,
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
                    commands.AddRange(Apply(new OrderFilled(time.GetUtcNow(), LiveRunMapping.RunId(live.Number), grown, price, filled, fee, Asset(order.FeeCoin))));

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
                        commands.AddRange(Apply(Cancelled(live)));

                        break;
                    case ExchangeOrderState.Rejected:
                        LiveOrder rejected = next.Rejected(order.RejectReason ?? "The exchange rejected the order.");
                        await SaveOrderAsync(rejected, token);
                        _bot = _bot.Fail(rejected.RejectReason!);
                        commands.AddRange(Apply(new OrderRejected(time.GetUtcNow(), LiveRunMapping.RunId(live.Number))));

                        break;
                    default:
                        await SaveOrderAsync(next, token);

                        break;
                }

                return commands;
            },
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
                LiveOrder before = _orders[number];
                LiveOrder ended = end(before);
                await SaveOrderAsync(ended, token);

                if (ended.RejectReason is { } reason)
                {
                    _bot = _bot.Fail(reason);

                    return Apply(new OrderRejected(time.GetUtcNow(), LiveRunMapping.RunId(number)));
                }

                return Apply(Cancelled(before));
            },
            cancellationToken);
    }

    /// <summary>A cancel the bot asked for is told apart from one it did not ask for: only the latter brings back the exit of a closing position.</summary>
    private StrategyEvent Cancelled(LiveOrder order)
    {
        DateTimeOffset at = time.GetUtcNow();
        string id = LiveRunMapping.RunId(order.Number);

        return order.Cancelling ? new OrderCancelled(at, id) : new OrderCancelledByVenue(at, id);
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

    private Task WriteAsync(Func<CancellationToken, Task<IReadOnlyList<OrderCommand>>> change, CancellationToken cancellationToken)
    {
        return WriteAsync(change, bot => bot, cancellationToken);
    }

    /// <summary>A unit of work that changes only the bot.</summary>
    private Task WriteBotAsync(Func<LiveBot, LiveBot> bot, CancellationToken cancellationToken)
    {
        return WriteAsync(_ => NoCommands, bot, cancellationToken);
    }

    /// <summary>The current position of the run, and the one it closed with this change.</summary>
    private async Task SavePositionsAsync(LivePosition? before, CancellationToken cancellationToken)
    {
        RunState state = _run.State;

        if (LiveRunMapping.Closed(before, state) is { } closed)
        {
            await repository.SavePositionAsync(closed, cancellationToken);
        }

        await repository.SavePositionAsync(LiveRunMapping.Current(_id, _run.Snapshot(), state), cancellationToken);
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
                        LiveRunMapping.Number(place.OrderId),
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
                case CancelOrder cancel when _orders.TryGetValue(LiveRunMapping.Number(cancel.OrderId), out LiveOrder? target):
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

        if (order.Unfinished)
        {
            _orders[order.Number] = order;
        }
        else
        {
            _orders.Remove(order.Number);
        }
    }

    /// <summary>Sends the saved orders and cancels that are due, by number, outside any unit of work.</summary>
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        LiveOrder[] due =
        [
            .. _orders.Values
                .Where(order => order.DueAt(now))
                .OrderBy(order => order.Number)
        ];

        foreach (LiveOrder order in due)
        {
            executor.EnsureNoTransaction();
            string code = order.ClientId.Code;
            OrderOutcome outcome = order.Status == LiveOrderStatus.PendingSubmit
                ? await gateway.PlaceAsync(_context.Key, _context.Bot.Instrument, new OrderRequest(code, order.Side, order.Type, order.Price, order.Quantity, order.Quantity is null ? order.Amount : null), cancellationToken)
                : await gateway.CancelAsync(_context.Key, _context.Bot.Instrument, code, cancellationToken);

            switch (OrderSending.Resolve(order.Status == LiveOrderStatus.PendingSubmit, outcome.Kind))
            {
                case SendStep.Taken:
                    journal.Sent(_id, code);
                    await WriteOrderAsync(order.Taken(outcome.ExchangeId), cancellationToken);

                    break;
                case SendStep.CancelTaken:
                    await WriteOrderAsync(order.CancelTaken(), cancellationToken);

                    break;
                case SendStep.CancelTakenAndRefresh:
                    await WriteOrderAsync(order.CancelTaken(), cancellationToken);
                    await RefreshAsync(order.Number, cancellationToken);

                    break;
                case SendStep.Deferred:
                    // The exchange may name how long another attempt is useless, such as until its next session opens.
                    LiveOrder deferred = order.Deferred(now, outcome.RetryAfter);
                    await WriteOrderAsync(deferred, cancellationToken);
                    _ = LaterAsync(new WakeInput(), deferred.NextAttemptAt!.Value - now, cancellationToken);

                    break;
                case SendStep.Rejected:
                    journal.Refused(_id, code, outcome.Reason ?? "refused");
                    await EndAsync(order.Number, live => live.Rejected(outcome.Reason ?? "The exchange refused the order."), cancellationToken);

                    break;
                case SendStep.CancelRefused:
                    journal.Refused(_id, code, outcome.Reason ?? "refused");

                    // The order stays on the exchange and is never left unwatched: the bot stops with the reason and asks
                    // again ever more slowly, until the exchange takes the cancel or the owner cancels the order there.
                    LiveOrder paused = order.Deferred(now);
                    await WriteOrderAsync(
                        paused,
                        bot => bot.Fail($"The exchange refused to cancel order {code}: {outcome.Reason} Cancel it on the exchange: the bot stops once the exchange shows the order done."),
                        cancellationToken);
                    _ = LaterAsync(new WakeInput(), paused.NextAttemptAt!.Value - now, cancellationToken);

                    break;
            }
        }
    }

    private Task WriteOrderAsync(LiveOrder order, CancellationToken cancellationToken)
    {
        return WriteOrderAsync(order, bot => bot, cancellationToken);
    }

    /// <summary>A unit of work that saves one order, stamped with the time of the write, and changes the bot.</summary>
    private Task WriteOrderAsync(LiveOrder order, Func<LiveBot, LiveBot> bot, CancellationToken cancellationToken)
    {
        return WriteAsync(
            async token =>
            {
                await SaveOrderAsync(order with { UpdatedAt = time.GetUtcNow() }, token);

                return [];
            },
            bot,
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
                        cancellationToken);
                    await SendAsync(cancellationToken);

                    return false;
                }

                if (_orders.Count == 0)
                {
                    await WriteBotAsync(bot => bot.Stopped(), cancellationToken);
                }

                return false;
            case LiveBotStatus.Stopped or LiveBotStatus.Failed when _bot.CloseRequested:
                if (_run.State.Status is PositionStatus.Open && _orders.Count == 0)
                {
                    // The row is read again under the lock: a renewal may have brought the request of a close the bot gave up since.
                    await WriteAsync(_ => Task.FromResult(_bot.CloseRequested ? _run.Close(time.GetUtcNow()) : []), cancellationToken);
                    await SendAsync(cancellationToken);

                    return false;
                }

                if (_run.State.Status is not (PositionStatus.Open or PositionStatus.Closing) && _orders.Count == 0)
                {
                    await WriteBotAsync(bot => bot.Closed(), cancellationToken);

                    return true;
                }

                return false;
            case LiveBotStatus.Stopped or LiveBotStatus.Failed:
                return true;
            default:
                return false;
        }
    }
}