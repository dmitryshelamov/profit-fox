using System.Collections.Concurrent;
using System.Threading.Channels;

using ProfitFox.Application.LiveTrading;
using ProfitFox.Application.LiveTrading.Runtime;
using ProfitFox.Application.Ports.LiveTrading;
using ProfitFox.Application.Ports.MarketData;
using ProfitFox.Application.Ports.Security;
using ProfitFox.Application.Transactions;
using ProfitFox.Application.Workers;
using ProfitFox.Domain.Entities.ExchangeAccounts;
using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.Entities.IdeaTrades.Enums;
using ProfitFox.Domain.Entities.LiveTrading.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Entities.Strategies.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.IdeaTrades;
using ProfitFox.Domain.ValueObjects.IdeaTrades;
using ProfitFox.Domain.ValueObjects.LiveTrading;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.IdeaTrades.Runtime;

/// <summary>
///     Keeps one trade by an idea while this Worker holds its lease: sends its saved entry, follows the entry and the stop and the target
///     the exchange made for it, cancels the entry at the end of its time or on request, and closes the position by the market when the
///     owner asks. As a bot's session does, it takes its inputs one at a time from a queue, writes every change in one unit of work after
///     checking the lease, and sends orders only once they are written.
/// </summary>
/// <remarks>
///     <c>_trade</c>, <c>_orders</c> and <c>_children</c> change inside a unit of work and run ahead of the database when it rolls back:
///     a failure of an input is followed by <see cref="RebuildAsync" />, and so are a gap in the stream and a restart.
/// </remarks>
internal sealed class IdeaTradeSession(
    LeasedIdeaTrade leased,
    IIdeaTradeRepository repository,
    ILiveBotRepository bots,
    IStrategyLibraryRepository library,
    IExchangeAccountRepository accounts,
    ISecretProtector protector,
    IExchangeLedger ledger,
    IOrderGateway gateway,
    IExchangeStreams streams,
    IInstrumentRulesProvider rules,
    IInstrumentCatalog catalog,
    ITransactionExecutor executor,
    ILiveTradingJournal journal,
    IdeaTradeLeases leases,
    WorkerSettings settings,
    WorkerIdentity worker,
    TimeProvider time)
{
    // How many times the books are compared with the exchange before the bots and the trades of the account and the symbol fail.
    private const int ReconcileAttempts = 3;

    // A market entry is sized at the click: Bybit has to take it within this time, or the price it was sized at is stale.
    private static readonly TimeSpan EntryWindow = TimeSpan.FromSeconds(30);

    // How soon a cancel of the stop or the target that Bybit met with a pause is asked again.
    private static readonly TimeSpan ChildCancelRetry = TimeSpan.FromSeconds(5);

    private readonly Channel<IdeaTradeInput> _inputs = Channel.CreateUnbounded<IdeaTradeInput>(new UnboundedChannelOptions { SingleReader = true });

    // The unfinished orders of the trade by number.
    private readonly Dictionary<int, IdeaTradeOrder> _orders = [];

    // The stops and targets the exchange made for the entry, by the exchange's id; the stream reads the keys from its own thread.
    private readonly ConcurrentDictionary<string, IdeaTradeChild> _children = new ConcurrentDictionary<string, IdeaTradeChild>();

    // The stops and targets Bybit refused to cancel for good: the owner is told, and nobody asks again.
    private readonly HashSet<string> _childRefused = [];

    private readonly Guid _id = leased.Trade.Id;
    private readonly LiveBotReconciler _reconciler = new LiveBotReconciler(bots, repository, library, ledger, catalog, executor, journal, worker, time);
    private bool _reconcileScheduled;
    private IdeaTrade _trade = leased.Trade;
    private TradeContext _context = null!;
    private bool _fillsKept;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // The Worker renews the lease and reads the trade for all its sessions; a lost lease ends this one.
        using IDisposable held = leases.Hold(
            leased,
            trade => _inputs.Writer.TryWrite(new StatusInput(trade)),
            () =>
            {
                _ = lease.CancelAsync();
                journal.Lost(_id);
            });
        IAsyncDisposable? subscription = null;
        journal.Took(_id, leased.Epoch);

        try
        {
            _context = await ContextAsync(lease.Token);
            subscription = await streams.SubscribeAccountAsync(
                _context.Key,
                order =>
                {
                    if (Mine(order))
                    {
                        _inputs.Writer.TryWrite(new OrderInput(order));
                    }
                },
                fill =>
                {
                    if (fill.Kind != ExchangeFillKind.Trade && fill.Instrument == _trade.Instrument)
                    {
                        // The exchange closed a position of the symbol by itself, with an order of its own: an exit, not a foreign fill.
                        _inputs.Writer.TryWrite(new ForcedFillInput(fill));
                    }
                    else if (Mine(fill))
                    {
                        _inputs.Writer.TryWrite(new FillInput(fill));
                    }
                    else if (!ClientOrderId.TryParse(fill.ClientId, out _) && !IdeaTradeOrderId.TryParse(fill.ClientId, out _) && fill.Instrument == _trade.Instrument)
                    {
                        // An execution of nobody here, or of the stop or the target of another trade, which has no client id: the database tells.
                        _inputs.Writer.TryWrite(new ForeignFillInput(fill.ExchangeOrderId));
                    }
                },
                () => _inputs.Writer.TryWrite(new RestoreInput()),
                lease.Token);
            await RebuildAsync(lease.Token);

            while (!await AdvanceAsync(lease.Token))
            {
                IdeaTradeInput input = await _inputs.Reader.ReadAsync(lease.Token);

                try
                {
                    await HandleAsync(input, lease.Token);
                }
                catch (Exception exception) when (exception is not (LeaseLostException or OperationCanceledException))
                {
                    // The books may have taken what the database did not: build them again from the database.
                    await RebuildAsync(lease.Token);
                }
            }

            await repository.ReleaseAsync(_id, worker.Id, leased.Epoch, CancellationToken.None);
        }
        catch (LeaseLostException)
        {
            journal.Lost(_id);
        }
        catch (OperationCanceledException) when (lease.IsCancellationRequested)
        {
            // The Worker stops or the lease is lost; the lease expires for any Worker.
        }
        catch (Exception)
        {
            // An exchange that does not answer, a failure of the session, or a cancellation that is not its own: the trade goes back to
            // any Worker after a pause, and the Worker logs the failure.
            await leases.HandBackAsync(_id, leased.Epoch);

            throw;
        }
        finally
        {
            await lease.CancelAsync();

            if (subscription is not null)
            {
                await subscription.DisposeAsync();
            }
        }
    }

    /// <summary>Whether an order update is about this trade: its own order, or the stop or the target the exchange made for its entry.</summary>
    private bool Mine(ExchangeOrder order)
    {
        return IsOwnCode(order.ClientId) || IsChildOfThisTrade(order) || _children.ContainsKey(order.ExchangeId);
    }

    private bool Mine(ExchangeFill fill)
    {
        return IsOwnCode(fill.ClientId) || (fill.ExchangeOrderId is not null && _children.ContainsKey(fill.ExchangeOrderId));
    }

    private bool IsOwnCode(string clientId)
    {
        return IdeaTradeOrderId.TryParse(clientId, out IdeaTradeOrderId? id) && id.TradeId == _id;
    }

    /// <summary>
    ///     The stop or the target Bybit made for an entry of this trade. On <c>linear</c> it names its entry in <c>parentOrderLinkId</c>. How a
    ///     child of an entry on the spot is told is not settled: it waits for the check of decision 9 of the design, so nothing is attributed there.
    /// </summary>
    private bool IsChildOfThisTrade(ExchangeOrder order)
    {
        return _trade.Instrument.Category == MarketCategory.Linear && IsOwnCode(order.ParentClientId ?? "");
    }

    private async Task<TradeContext> ContextAsync(CancellationToken cancellationToken)
    {
        ExchangeAccount account = await accounts.GetAsync(_trade.AccountId, cancellationToken)
                                  ?? throw new InvalidOperationException($"Trade {_id} trades with an account that is gone.");
        ExchangeKey key = await LiveRequests.KeyAsync(accounts, protector, account, cancellationToken);
        InstrumentRules instrumentRules = await rules.GetRulesAsync(_trade.Instrument, cancellationToken);
        string baseCoin = (await catalog.ListAsync(_trade.Instrument.Category, cancellationToken)).Single(item => item.Symbol == _trade.Instrument.Symbol).BaseCoin;

        return new TradeContext(_trade.Instrument, key, baseCoin, instrumentRules);
    }

    /// <summary>
    ///     Builds the books again from the database, then reads what the exchange did meanwhile: the totals of the unfinished orders, of the
    ///     stop and the target, and the entry when the stop or the target is told before it.
    /// </summary>
    private async Task RebuildAsync(CancellationToken cancellationToken)
    {
        await executor.ExecuteAsync(
            async token =>
            {
                _trade = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();
                _orders.Clear();

                foreach (IdeaTradeOrder order in await repository.GetUnfinishedOrdersAsync(_id, token))
                {
                    _orders[order.Number] = order;
                }

                return 0;
            },
            cancellationToken);
        _children.Clear();
        _childRefused.Clear();

        foreach (IdeaTradeChild child in await repository.GetChildrenAsync(_id, cancellationToken))
        {
            _children[child.ExchangeOrderId] = child;
        }

        await CatchUpAsync(cancellationToken);
        await ReconcileAsync(cancellationToken);
        ScheduleDeadline(cancellationToken);
    }

    /// <summary>Compares the books with the exchange and fails the trades of a difference that stays after the exchange is read again.</summary>
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!_trade.IsOpen)
        {
            return;
        }

        (IReadOnlyList<Guid> neighbours, IReadOnlyList<Guid> trades, IReadOnlyList<string> differences) = await _reconciler.CompareAsync(_trade, _context.Key, _context.BaseCoin, _context.Rules, cancellationToken);

        // A fill may land between the catch-up and the read of the exchange, and a neighbour may write its fill a moment after the
        // exchange shows it: the books are read again right away and once more after a pause before anything fails.
        for (var attempt = 1; differences.Count > 0 && attempt < ReconcileAttempts; attempt++)
        {
            if (attempt > 1)
            {
                await Task.Delay(settings.PollInterval * 3, time, cancellationToken);
            }

            await CatchUpAsync(cancellationToken);
            (neighbours, trades, differences) = await _reconciler.CompareAsync(_trade, _context.Key, _context.BaseCoin, _context.Rules, cancellationToken);
        }

        if (differences.Count > 0)
        {
            _trade = await _reconciler.FailAllAsync(leased, neighbours, trades, string.Join(" ", differences), cancellationToken) ?? _trade;
        }
    }

    private async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        foreach (IdeaTradeOrder order in _orders.Values.ToArray())
        {
            await RefreshAsync(order.Number, cancellationToken);
        }

        await RefreshChildrenAsync(cancellationToken);

        // The entry is filled and its stop and target are not both known yet: the exchange may have them, open or finished, naming the entry.
        if (_trade.Status == IdeaTradeStatus.Open && _children.Values.Select(child => child.Leg).Distinct().Count() < 2)
        {
            foreach (ExchangeOrder child in await ledger.GetOrdersOfParentAsync(_context.Key, _context.Instrument, new IdeaTradeOrderId(_id, 1).Code, cancellationToken))
            {
                await OrderAsync(child, cancellationToken);
            }
        }
    }

    /// <summary>Reads the stop and the target that are not over in the books: one may have fired or been cancelled unheard.</summary>
    private async Task RefreshChildrenAsync(CancellationToken cancellationToken)
    {
        foreach (IdeaTradeChild child in _children.Values.Where(child => !child.Finished).ToArray())
        {
            if (await ledger.FindOrderByExchangeIdAsync(_context.Key, _context.Instrument, child.ExchangeOrderId, cancellationToken) is { } known)
            {
                await OrderAsync(known, cancellationToken);
            }
        }
    }

    /// <summary>Applies what the exchange knows of one order of the trade: its totals, then its trades as detail.</summary>
    private async Task RefreshAsync(int number, CancellationToken cancellationToken)
    {
        if (!_orders.TryGetValue(number, out IdeaTradeOrder? order))
        {
            return;
        }

        string code = order.ClientId.Code;
        ExchangeOrder? known = await ledger.FindOrderAsync(_context.Key, _context.Instrument, code, cancellationToken);

        if (known is null)
        {
            // A cancel of an order the exchange never took ends the order.
            if (order.Status == LiveOrderStatus.CancelSent)
            {
                await EndAsync(order.Number, ended => ended.Cancelled(), cancellationToken);
            }

            return;
        }

        await OrderAsync(known, cancellationToken);

        foreach (ExchangeFill trade in await ledger.GetFillsAsync(_context.Key, _context.Instrument, code, cancellationToken))
        {
            await TradeAsync(trade, cancellationToken);
        }
    }

    private async Task HandleAsync(IdeaTradeInput input, CancellationToken cancellationToken)
    {
        switch (input)
        {
            case OrderInput { Order: var order }:
                await OrderAsync(order, cancellationToken);

                break;
            case FillInput { Fill: var fill }:
                await TradeAsync(fill, cancellationToken);

                break;
            case ForcedFillInput { Fill: var forced }:
                await ForcedAsync(forced, cancellationToken);

                break;
            case RestoreInput:
                await RebuildAsync(cancellationToken);

                break;
            case ForeignFillInput foreign when !_reconcileScheduled:
                if (foreign.ExchangeOrderId is { } exchangeId && await repository.IsChildAsync(exchangeId, cancellationToken))
                {
                    break;
                }

                // The REST reads may lag behind the stream: the comparison waits as long as the one repeated after a difference.
                _reconcileScheduled = true;
                _ = LaterAsync(settings.PollInterval * 3, cancellationToken, reconcile: true);

                break;
            case ReconcileInput:
                _reconcileScheduled = false;
                await ReconcileAsync(cancellationToken);

                break;
            case StatusInput { Trade: var trade }:
                // A renewal may read the row before the last write of this session: what the session wrote stays.
                _trade = _trade with
                {
                    CancelRequested = _trade.CancelRequested || trade.CancelRequested,
                    CloseRequested = _trade.CloseRequested || trade.CloseRequested,
                    LastOrderNumber = Math.Max(trade.LastOrderNumber, _trade.LastOrderNumber)
                };

                break;
        }

        await SendAsync(cancellationToken);
    }

    /// <summary>A trade of the exchange is kept with the trade by an idea once, by the exchange's id of it, as detail only.</summary>
    private async Task TradeAsync(ExchangeFill fill, CancellationToken cancellationToken)
    {
        // Bybit names the order of every execution it reports, on the stream and in its history.
        if (fill.ExchangeOrderId is not { } exchangeId)
        {
            return;
        }

        await WriteAsync(
            async token =>
            {
                await repository.AddFillAsync(
                    new IdeaTradeFill(fill.ExecId, _id, exchangeId, fill.Quantity, fill.Price, fill.Fee, fill.FeeCoin == _context.BaseCoin ? FeeAsset.Base : FeeAsset.Quote, fill.At),
                    token);
            },
            cancellationToken);
    }

    /// <summary>
    ///     A liquidation or an auto-deleveraging of the exchange that closes the side of the trade. Under the locks of the open trades of that side,
    ///     taken in the order of their ids, the execution is split between them once; then this trade applies its own share, once. The shares of
    ///     the other trades are theirs to apply.
    /// </summary>
    private async Task ForcedAsync(ExchangeFill fill, CancellationToken cancellationToken)
    {
        IdeaSide closed = fill.Side == OrderSide.Sell ? IdeaSide.Long : IdeaSide.Short;

        if (_trade.Status != IdeaTradeStatus.Open || _trade.Side != closed)
        {
            return;
        }

        IdeaTradeLeg leg = fill.Kind == ExchangeFillKind.Liquidation ? IdeaTradeLeg.Liquidation : IdeaTradeLeg.Adl;
        await executor.ExecuteAsync(
            async token =>
            {
                IReadOnlyList<IdeaTrade> holders = await repository.LockHoldersAsync(_trade.AccountId, _trade.Instrument, closed, token);
                _trade = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();

                if (!await repository.HasForcedSharesAsync(fill.ExecId, token))
                {
                    IReadOnlyList<ForcedFillShare> shares = ForcedFillShares.Split(
                        fill.Quantity,
                        fill.Price,
                        fill.Fee,
                        _context.Rules.QuantityStep,
                        [.. holders.Select(holder => new ForcedFillHolder(holder.Id, holder.HeldQuantity))]);
                    await repository.AddForcedSharesAsync([.. shares.Select(share => new IdeaTradeForcedShare(fill.ExecId, share.TradeId, leg, share.Totals, fill.At))], token);
                }

                if (await repository.GetUnappliedForcedShareAsync(fill.ExecId, _id, token) is { } own)
                {
                    _trade = _trade.ExitGrown(own.Leg, own.Totals, time.GetUtcNow());
                    await repository.MarkForcedShareAppliedAsync(fill.ExecId, _id, token);
                    await repository.SaveAsync(_trade, time.GetUtcNow(), token);
                }

                return 0;
            },
            cancellationToken);
    }

    /// <summary>
    ///     What the exchange says of an order of the trade, from its stream or its REST: what its totals grew by is a fill of the
    ///     books, a repeat grows them by nothing; then its end. The stop and the target are told apart from the entry by their parent.
    /// </summary>
    private async Task OrderAsync(ExchangeOrder order, CancellationToken cancellationToken)
    {
        if (IdeaTradeOrderId.TryParse(order.ClientId, out IdeaTradeOrderId? id) && id.TradeId == _id)
        {
            if (_orders.TryGetValue(id.Number, out IdeaTradeOrder? own))
            {
                await OwnOrderAsync(order, own, cancellationToken);
            }

            return;
        }

        if (IsChildOfThisTrade(order) || _children.ContainsKey(order.ExchangeId))
        {
            await ChildAsync(order, cancellationToken);
        }
    }

    private async Task OwnOrderAsync(ExchangeOrder order, IdeaTradeOrder own, CancellationToken cancellationToken)
    {
        decimal grown = order.FilledQuantity - own.FilledQuantity;

        if (grown <= 0m && order.State == ExchangeOrderState.Open && own.Status != LiveOrderStatus.PendingSubmit)
        {
            return;
        }

        if (order.State == ExchangeOrderState.Rejected)
        {
            journal.Refused(_id, own.ClientId.Code, order.RejectReason ?? "rejected");
        }

        await WriteAsync(
            async token =>
            {
                DateTimeOffset now = time.GetUtcNow();
                IdeaTradeOrder next = own.Taken(order.ExchangeId);

                if (grown > 0m)
                {
                    next = next.Grown(order.FilledQuantity, order.FilledValue, order.Fee, order.State == ExchangeOrderState.Filled);
                    LegTotals totals = Totals(order, own.Side == OrderSide.Buy);
                    _trade = own.Role == IdeaTradeOrderRole.Entry
                        ? _trade.EntryGrown(totals, now)
                        : _trade.ExitGrown(IdeaTradeLeg.Manual, totals - new LegTotals(own.FilledQuantity, own.FilledValue, own.Fee), now);
                }

                switch (order.State)
                {
                    case ExchangeOrderState.Filled:
                        next = next.Completed();

                        break;
                    case ExchangeOrderState.Cancelled:
                        next = next.Cancelled();

                        if (own.Role == IdeaTradeOrderRole.Entry)
                        {
                            _trade = _trade.EntryEnded(now) with { CancelRequested = false };
                        }
                        else
                        {
                            _trade = _trade with { Reason = $"Закрытие по рынку отменено на бирже, осталось {_trade.HeldQuantity}. Стоп и цель стоят, закрытие можно повторить." };
                        }

                        break;
                    case ExchangeOrderState.Rejected:
                        next = next.Rejected(order.RejectReason ?? "Bybit rejected the order.");
                        _trade = Refused(next, now);

                        break;
                }

                await SaveOrderAsync(next, token);
            },
            cancellationToken);
    }

    /// <summary>The stop or the target of the entry: kept by the exchange's id, its totals move the books as an exit.</summary>
    private async Task ChildAsync(ExchangeOrder order, CancellationToken cancellationToken)
    {
        IdeaTradeLeg? known = order.StopKind switch
        {
            StopOrderKind.TakeProfit => IdeaTradeLeg.TakeProfit,
            StopOrderKind.StopLoss => IdeaTradeLeg.StopLoss,
            _ => _children.TryGetValue(order.ExchangeId, out IdeaTradeChild? child) ? child.Leg : null
        };

        if (known is not { } leg)
        {
            return;
        }

        await WriteAsync(
            async token =>
            {
                LegTotals totals = Totals(order, false);
                LegTotals before = _children.TryGetValue(order.ExchangeId, out IdeaTradeChild? counted) ? new LegTotals(counted.FilledQuantity, counted.FilledValue, counted.Fee) : LegTotals.None;
                var saved = new IdeaTradeChild(_id, order.ExchangeId, leg, order.FilledQuantity, order.FilledValue, totals.Fee, order.State != ExchangeOrderState.Open);
                await repository.SaveChildAsync(saved, token);
                _children[order.ExchangeId] = saved;
                _trade = _trade.ExitGrown(leg, totals - before, time.GetUtcNow());
            },
            cancellationToken);
    }

    /// <summary>
    ///     The totals of an order for the books. The fee in the base coin of a spot buy is taken from the coins bought: they are fewer by it,
    ///     and it is worth its quantity at the average price.
    /// </summary>
    private LegTotals Totals(ExchangeOrder order, bool buying)
    {
        if (order.FeeCoin == _context.BaseCoin && order.FilledQuantity > 0m)
        {
            return new LegTotals(buying ? order.FilledQuantity - order.Fee : order.FilledQuantity, order.FilledValue, order.Fee * order.FilledValue / order.FilledQuantity);
        }

        return new LegTotals(order.FilledQuantity, order.FilledValue, order.Fee);
    }

    /// <summary>Ends an order with no fill to come; a refusal fails the trade with its reason.</summary>
    private Task EndAsync(int number, Func<IdeaTradeOrder, IdeaTradeOrder> end, CancellationToken cancellationToken)
    {
        return WriteAsync(
            async token =>
            {
                IdeaTradeOrder ended = end(_orders[number]);
                await SaveOrderAsync(ended, token);

                if (ended.RejectReason is not null)
                {
                    _trade = Refused(ended, time.GetUtcNow());
                }
                else if (ended.Status == LiveOrderStatus.Cancelled && ended.Role == IdeaTradeOrderRole.Entry)
                {
                    _trade = _trade.EntryEnded(time.GetUtcNow()) with { CancelRequested = false };
                }
            },
            cancellationToken);
    }

    /// <summary>
    ///     What a refusal of an order does to the trade: a refused entry fails it; a refused close leaves it open, its stop and target standing,
    ///     with the reason for the owner, who may close again.
    /// </summary>
    private IdeaTrade Refused(IdeaTradeOrder refused, DateTimeOffset now)
    {
        return refused.Role == IdeaTradeOrderRole.Close
            ? _trade with { Reason = $"Закрытие по рынку отклонено: {refused.RejectReason} Стоп и цель стоят, закрытие можно повторить." }
            : _trade.Fail(refused.RejectReason!, now);
    }

    /// <summary>One unit of work: the lease is checked first, then <paramref name="change" /> writes, and the trade is written last.</summary>
    private async Task WriteAsync(Func<CancellationToken, Task> change, CancellationToken cancellationToken, Func<IdeaTrade, IdeaTrade>? trade = null)
    {
        await executor.ExecuteAsync(
            async token =>
            {
                _trade = await repository.LockLeasedAsync(_id, worker.Id, leased.Epoch, token) ?? throw new LeaseLostException();
                await change(token);
                _trade = trade is null ? _trade : trade(_trade);
                await repository.SaveAsync(_trade, time.GetUtcNow(), token);

                return 0;
            },
            cancellationToken);
    }

    private async Task SaveOrderAsync(IdeaTradeOrder order, CancellationToken cancellationToken)
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
        IdeaTradeOrder[] due = [.. _orders.Values.Where(order => order.DueAt(now)).OrderBy(order => order.Number)];

        foreach (IdeaTradeOrder order in due)
        {
            executor.EnsureNoTransaction();
            string code = order.ClientId.Code;
            bool placing = order.Status == LiveOrderStatus.PendingSubmit;

            if (placing && order.Role == IdeaTradeOrderRole.Entry && order.Type == OrderType.Market && now - _trade.CreatedAt > EntryWindow)
            {
                await GiveUpEntryAsync(order, cancellationToken);

                continue;
            }

            OrderOutcome outcome = placing
                ? await gateway.PlaceAsync(_context.Key, _context.Instrument, Request(order), cancellationToken)
                : await gateway.CancelAsync(_context.Key, _context.Instrument, code, cancellationToken);

            switch (OrderSending.Resolve(placing, outcome.Kind))
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
                    IdeaTradeOrder deferred = order.Deferred(now);
                    await WriteOrderAsync(deferred, cancellationToken);
                    _ = LaterAsync(deferred.NextAttemptAt!.Value - now, cancellationToken);

                    break;
                case SendStep.Rejected:
                    journal.Refused(_id, code, outcome.Reason ?? "refused");
                    await EndAsync(order.Number, ended => ended.Rejected(outcome.Reason ?? "Bybit refused the order."), cancellationToken);

                    break;
                case SendStep.CancelRefused:
                    journal.Refused(_id, code, outcome.Reason ?? "refused");

                    // The order stays on the exchange: the trade fails with the reason, and the session asks again ever more slowly until the order ends.
                    IdeaTradeOrder paused = order.Deferred(now);
                    await WriteOrderAsync(paused, cancellationToken, $"Bybit refused to cancel order {code}: {outcome.Reason} Cancel it on Bybit.");
                    _ = LaterAsync(paused.NextAttemptAt!.Value - now, cancellationToken);

                    break;
            }
        }
    }

    /// <summary>
    ///     A market entry that Bybit did not take in time is not sent: the price it was sized at is stale. The answer to a send may have been
    ///     lost while Bybit took the order, so the exchange is asked for it first.
    /// </summary>
    private async Task GiveUpEntryAsync(IdeaTradeOrder entry, CancellationToken cancellationToken)
    {
        if (await ledger.FindOrderAsync(_context.Key, _context.Instrument, entry.ClientId.Code, cancellationToken) is { } taken)
        {
            await OrderAsync(taken, cancellationToken);

            return;
        }

        await WriteAsync(
            async token =>
            {
                await SaveOrderAsync((_orders.TryGetValue(entry.Number, out IdeaTradeOrder? current) ? current : entry).Cancelled() with { UpdatedAt = time.GetUtcNow() }, token);
                _trade = _trade.EntryEnded(time.GetUtcNow()) with { Reason = "Вход не ушёл вовремя: Bybit не принял его за 30 секунд.", CancelRequested = false };
            },
            cancellationToken);
    }

    private static OrderRequest Request(IdeaTradeOrder order)
    {
        return new OrderRequest(order.ClientId.Code, order.Side, order.Type, order.Price, order.Quantity, null, order.Protection, order.TimeInForce, order.ReduceOnly);
    }

    /// <summary>A unit of work that saves one order, stamped with the time of the write, and fails the trade when told a reason.</summary>
    private Task WriteOrderAsync(IdeaTradeOrder order, CancellationToken cancellationToken, string? failure = null)
    {
        return WriteAsync(
            async token =>
            {
                await SaveOrderAsync(order with { UpdatedAt = time.GetUtcNow() }, token);

                if (failure is not null)
                {
                    _trade = _trade.Fail(failure, time.GetUtcNow());
                }
            },
            cancellationToken);
    }

    private async Task LaterAsync(TimeSpan delay, CancellationToken cancellationToken, bool reconcile = false)
    {
        try
        {
            await Task.Delay(delay, time, cancellationToken);
            _inputs.Writer.TryWrite(reconcile ? new ReconcileInput() : new WakeInput());
        }
        catch (OperationCanceledException)
        {
            // The session ended.
        }
    }

    /// <summary>Wakes the session when the entry of a pullback runs out of time.</summary>
    private void ScheduleDeadline(CancellationToken cancellationToken)
    {
        if (_trade.EntryDeadline is not { } deadline)
        {
            return;
        }

        _ = LaterAsync(deadline > time.GetUtcNow() ? deadline - time.GetUtcNow() : TimeSpan.Zero, cancellationToken);
    }

    /// <summary>
    ///     Moves the trade on by its status: cancels the entry when its time is out or the owner asks, closes the position by the market
    ///     when the owner asks. Returns whether the session is done with the trade.
    /// </summary>
    private async Task<bool> AdvanceAsync(CancellationToken cancellationToken)
    {
        if (!_trade.IsOpen)
        {
            return await WindDownAsync(cancellationToken);
        }

        IdeaTradeOrder? entry = _orders.Values.FirstOrDefault(order => order.Role == IdeaTradeOrderRole.Entry);

        if (entry is { Cancelling: false } && (_trade.CancelRequested || _trade.EntryExpired(time.GetUtcNow()) || _trade.CloseRequested))
        {
            await RequestCancelAsync(entry.Number, cancellationToken);
        }
        else if (_trade.CloseRequested && entry is null)
        {
            await CloseAsync(cancellationToken);
        }
        else if (_trade.CancelRequested && entry is null)
        {
            // The entry filled before the cancel came: the trade is open, and the request has nothing left to ask.
            await WriteAsync(_ => Task.CompletedTask, cancellationToken, trade => trade with { CancelRequested = false });
        }

        await SendAsync(cancellationToken);

        return false;
    }

    private Task RequestCancelAsync(int number, CancellationToken cancellationToken)
    {
        return WriteAsync(
            async token =>
            {
                if (_orders.TryGetValue(number, out IdeaTradeOrder? current) && !current.Cancelling)
                {
                    await SaveOrderAsync(current.RequestCancel(time.GetUtcNow()), token);
                }
            },
            cancellationToken);
    }

    /// <summary>
    ///     A trade that is over still has work while an order of it stands: the entry of a failed trade is taken off, since its stop and target
    ///     would guard a position nobody follows, and the stop and the target a market close left are taken off. Returns whether nothing is left.
    /// </summary>
    private async Task<bool> WindDownAsync(CancellationToken cancellationToken)
    {
        if (_trade.Status == IdeaTradeStatus.Failed && _orders.Values.FirstOrDefault(order => order.Role == IdeaTradeOrderRole.Entry) is { Cancelling: false } entry)
        {
            await RequestCancelAsync(entry.Number, cancellationToken);
        }

        if (!_fillsKept && _trade.Status is IdeaTradeStatus.ClosedAtTarget or IdeaTradeStatus.ClosedAtStop or IdeaTradeStatus.ClosedManually)
        {
            await KeepFillsAsync(cancellationToken);
        }

        var left = false;

        if (_trade.Status == IdeaTradeStatus.ClosedManually)
        {
            left = await TakeChildrenOffAsync(cancellationToken);
        }

        await SendAsync(cancellationToken);

        return !left && _orders.Count == 0;
    }

    /// <summary>
    ///     The executions of a trade that closed are kept with it: the session is done with the trade when the update of the last order has
    ///     closed it, and the executions come after that update.
    /// </summary>
    private async Task KeepFillsAsync(CancellationToken cancellationToken)
    {
        _fillsKept = true;
        IEnumerable<string> orders = (await repository.GetOrdersAsync(_id, cancellationToken)).Where(order => order.ExchangeOrderId is not null).Select(order => order.ExchangeOrderId!);

        foreach (string exchangeId in orders.Concat(_children.Keys))
        {
            foreach (ExchangeFill fill in await ledger.GetOrderFillsAsync(_context.Key, _context.Instrument, exchangeId, cancellationToken))
            {
                await TradeAsync(fill, cancellationToken);
            }
        }
    }

    /// <summary>
    ///     Asks the exchange to cancel the stop and the target that are not over, then reads them: a cancel Bybit meets with a pause is asked
    ///     again after a while, one it refuses for good is named to the owner.
    /// </summary>
    /// <returns>Whether a stop or a target is still to be seen cancelled.</returns>
    private async Task<bool> TakeChildrenOffAsync(CancellationToken cancellationToken)
    {
        foreach (IdeaTradeChild child in _children.Values.Where(child => !child.Finished && !_childRefused.Contains(child.ExchangeOrderId)).ToArray())
        {
            executor.EnsureNoTransaction();
            OrderOutcome outcome = await gateway.CancelByExchangeIdAsync(_context.Key, _context.Instrument, child.ExchangeOrderId, cancellationToken);

            if (outcome.Kind == OrderOutcomeKind.Permanent)
            {
                _childRefused.Add(child.ExchangeOrderId);
                journal.Refused(_id, child.ExchangeOrderId, outcome.Reason ?? "refused");
                await WriteAsync(_ => Task.CompletedTask, cancellationToken, trade => trade with { Reason = $"Bybit отказал снять ордер {child.ExchangeOrderId}: {outcome.Reason} Снимите его на Bybit." });
            }
        }

        await RefreshChildrenAsync(cancellationToken);
        bool left = _children.Values.Any(child => !child.Finished && !_childRefused.Contains(child.ExchangeOrderId));

        if (left)
        {
            _ = LaterAsync(ChildCancelRetry, cancellationToken);
        }

        return left;
    }

    /// <summary>Closes what the trade holds by the market with an order that only reduces the position; the stop and the target stand until it is done.</summary>
    private async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_orders.Values.Any(order => order.Role == IdeaTradeOrderRole.Close))
        {
            return;
        }

        // The stop or the target may have fired unheard: the close goes only if the position is still there.
        await RefreshChildrenAsync(cancellationToken);

        await WriteAsync(
            async token =>
            {
                if (!_trade.CloseRequested || _trade.Status != IdeaTradeStatus.Open)
                {
                    return;
                }

                int number = _trade.LastOrderNumber + 1;
                OrderSide side = _trade.Side == IdeaSide.Long ? OrderSide.Sell : OrderSide.Buy;
                await SaveOrderAsync(IdeaTradeOrder.NewClose(_id, number, side, _trade.HeldQuantity, time.GetUtcNow()), token);
                _trade = _trade with { LastOrderNumber = number, CloseRequested = false, Reason = null };
            },
            cancellationToken);
    }

    private sealed record TradeContext(Instrument Instrument, ExchangeKey Key, string BaseCoin, InstrumentRules Rules);
}