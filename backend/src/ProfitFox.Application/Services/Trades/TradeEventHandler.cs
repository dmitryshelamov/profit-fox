using Microsoft.Extensions.Logging;

using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Repositories;
using ProfitFox.Domain.Services.Trades;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Application.Services.Trades;

/// <summary>
///     Where a message of the exchange comes from: the stream, or a read of REST. The handler names it only in the journal.
/// </summary>
public enum MessageSource
{
    Stream,
    Rest
}

/// <summary>
///     What the handler wants of the exchange after a message: the stop of a trade put back on its position.
/// </summary>
public sealed record StopRequest(Guid TradeId, string Symbol, decimal Stop);

/// <summary>
///     The one handler of the messages of the exchange, by the stream and by REST alike (change take-idea-trades, design.md,
///     decisions 3, 5, 8 and 10). A repeat or a change of order makes no difference: a fill is written once by its id and
///     belongs to the trade that holds its pair — not to an order —, so a fill before its order opens the trade; an order only
///     goes forward; a snapshot of a position is compared with the quantity the fills of the trade hold. A trade in «Ошибка»
///     is not led: its position and its stop are not touched.
/// </summary>
public sealed class TradeEventHandler(ITradeRepository trades, TimeProvider time, ILogger logger)
{
    /// <summary>
    ///     How much earlier than its trade a fill may be told by the clock of the exchange and still belong to it.
    /// </summary>
    private static readonly TimeSpan Skew = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Takes a message of the account; a stop to put back when the position lost it.
    /// </summary>
    public async Task<StopRequest?> HandleAsync(string worker, Guid accountId, ExchangeMessage message, MessageSource source, IReadOnlyList<TradeBook> holding, CancellationToken cancellationToken)
    {
        switch (message)
        {
            case ExchangeFill fill:
                await FillAsync(worker, accountId, fill, source, holding, cancellationToken);
                return null;
            case ExchangeOrderUpdate order:
                await OrderAsync(worker, order, source, holding, cancellationToken);
                return null;
            case ExchangePosition position:
                return await PositionAsync(worker, accountId, position, source, holding, cancellationToken);
            default:
                return null;
        }
    }

    /// <summary>
    ///     The answer of the exchange to the stop put back: on the place the failures start anew, a refusal adds one, and the last
    ///     attempt writes the close «без стопа».
    /// </summary>
    public async Task StopAnsweredAsync(string worker, StopRequest request, bool placed, string? reason, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        await trades.ChangeAsync(request.TradeId, worker, book =>
        {
            Trade trade = book.Trade;
            if (trade.State is not (TradeState.Open or TradeState.Closing))
            {
                return false;
            }

            if (placed)
            {
                trade.StopFailures = 0;
                return true;
            }

            trade.StopFailures++;
            logger.LogWarning("Bybit did not take the stop {Stop} of trade {Trade} on {Symbol} ({Failures} of {Attempts}): {Reason}", request.Stop, trade.Id, trade.Symbol, trade.StopFailures, trade.StopAttempts, reason);
            // A trade that closes already keeps its close; an open one closes «без стопа».
            if (trade.StopFailures >= trade.StopAttempts)
            {
                AddClose(book, TradeCloseReason.NoStop, now);
            }

            return true;
        }, null, cancellationToken);
    }

    /// <summary>
    ///     Writes a close order of the reason for a trade that has none going and moves it to «Закрывается».
    /// </summary>
    public static bool AddClose(TradeBook book, TradeCloseReason reason, DateTimeOffset now)
    {
        if (book.Trade.State != TradeState.Open || book.Orders.Any(order => order.Purpose == TradeOrderPurpose.Close && order.Status is TradeOrderStatus.Pending or TradeOrderStatus.Sent))
        {
            return false;
        }

        book.Put(new TradeOrder(book.Trade.Id, book.NextNumber, TradeOrderPurpose.Close, reason, book.Trade.Numbers.Held, TradeOrderStatus.Pending, 0, now, null, null, now));
        book.Trade.StartClosing();
        return true;
    }

    /// <summary>
    ///     Writes a close order for what is still held when the last close order ended without closing the whole position —
    ///     the exchange filled it in part and cancelled the rest, or <paramref name="alive" />: it was told filled and the
    ///     position is still there —, with the reason of that order. False when a close order is still going, nothing is held,
    ///     or the last close did not end so.
    /// </summary>
    public static bool AddRest(TradeBook book, DateTimeOffset now, bool alive = false)
    {
        Trade trade = book.Trade;
        TradeOrder? last = book.Orders.Where(order => order.Purpose == TradeOrderPurpose.Close).MaxBy(order => order.Number);
        if (trade.State != TradeState.Closing || trade.Numbers.Held <= 0 || last is null || (last.Status != TradeOrderStatus.Cancelled && !(alive && last.Status == TradeOrderStatus.Filled))
            || book.Orders.Any(order => order.Purpose == TradeOrderPurpose.Close && order.Status is TradeOrderStatus.Pending or TradeOrderStatus.Sent))
        {
            return false;
        }

        book.Put(new TradeOrder(trade.Id, book.NextNumber, TradeOrderPurpose.Close, last.Reason, trade.Numbers.Held, TradeOrderStatus.Pending, 0, now, null, null, now));
        return true;
    }

    private async Task FillAsync(string worker, Guid accountId, ExchangeFill fill, MessageSource source, IReadOnlyList<TradeBook> holding, CancellationToken cancellationToken)
    {
        TradeBook? owner = Owner(accountId, fill.Symbol, fill.OrderLinkId, holding);
        if (owner is null || fill.Time < owner.Trade.CreatedAt - Skew)
        {
            return;
        }

        TradeBook? changed = await trades.ChangeAsync(owner.Trade.Id, worker, book =>
        {
            if (book.Known.Contains(fill.ExecId) || book.Fills.Any(kept => kept.ExecId == fill.ExecId))
            {
                return false;
            }

            TradeFill kept = FillKinds.ToTrade(fill, book.Trade, book.Orders);
            book.Fills.Add(kept);
            TradeState before = book.Trade.State;
            book.Trade.Apply(book.Fills);
            if (kept.Kind == FillKind.Entry && book.Trade.Numbers.Entered >= book.Trade.Quantity)
            {
                Move(book, 1, TradeOrderStatus.Filled, null);
            }

            if (book.Trade.State == TradeState.Closed)
            {
                foreach (TradeOrder order in book.Orders.Where(order => order.Purpose == TradeOrderPurpose.Close && order.Status is TradeOrderStatus.Pending or TradeOrderStatus.Sent))
                {
                    book.Put(order with { Status = order.Status == TradeOrderStatus.Pending ? TradeOrderStatus.Cancelled : TradeOrderStatus.Filled });
                }
            }

            if (book.Trade.State != before)
            {
                logger.LogInformation("Trade {Trade} on {Symbol} is {State} by fill {Exec} ({Source})", book.Trade.Id, book.Trade.Symbol, book.Trade.State, fill.ExecId, source);
            }

            return true;
        }, [fill.ExecId], cancellationToken);
        if (changed is { Trade.State: TradeState.Closed })
        {
            logger.LogInformation("Trade {Trade} on {Symbol} closed: {Reason}, {Result} USDT", changed.Trade.Id, changed.Trade.Symbol, changed.Trade.CloseReason, changed.Trade.Result);
        }
    }

    private async Task OrderAsync(string worker, ExchangeOrderUpdate update, MessageSource source, IReadOnlyList<TradeBook> holding, CancellationToken cancellationToken)
    {
        if (TradeOrderId.Parse(update.OrderLinkId) is not { } link || holding.All(book => book.Trade.Id != link.TradeId))
        {
            return;
        }

        DateTimeOffset now = time.GetUtcNow();
        await trades.ChangeAsync(link.TradeId, worker, book =>
        {
            if (book.Orders.FirstOrDefault(order => order.Number == link.Number) is not { } order)
            {
                return false;
            }

            // An order the exchange ended with a part filled: an entry is filled by what it got; a close is cancelled, and the
            // tick writes the close of the rest.
            TradeOrderStatus status = update.State switch
            {
                ExchangeOrderState.Filled => TradeOrderStatus.Filled,
                ExchangeOrderState.Rejected when update.Filled == 0 => TradeOrderStatus.Rejected,
                ExchangeOrderState.Cancelled when update.Filled == 0 => TradeOrderStatus.Cancelled,
                ExchangeOrderState.Rejected or ExchangeOrderState.Cancelled => order.Purpose == TradeOrderPurpose.Entry ? TradeOrderStatus.Filled : TradeOrderStatus.Cancelled,
                _ => TradeOrderStatus.Sent
            };
            if (!order.CanMoveTo(status))
            {
                return false;
            }

            Move(book, order.Number, status, update.OrderId);
            if (status is TradeOrderStatus.Rejected or TradeOrderStatus.Cancelled && update.Filled == 0)
            {
                string reason = $"Bybit: {update.Reason ?? "заявка отклонена"}";
                if (order.Purpose == TradeOrderPurpose.Entry && book.Trade.State == TradeState.Pending && book.Trade.Numbers.Entered == 0)
                {
                    book.Trade.Cancel(reason, now);
                }
                else if (order.Purpose == TradeOrderPurpose.Close && book.Trade.State == TradeState.Closing)
                {
                    book.Trade.CloseRefused(reason);
                }

                logger.LogWarning("Order {Order} of trade {Trade} is {Status} ({Source}): {Reason}", order.LinkId, book.Trade.Id, status, source, reason);
            }

            return true;
        }, null, cancellationToken);
    }

    private async Task<StopRequest?> PositionAsync(string worker, Guid accountId, ExchangePosition position, MessageSource source, IReadOnlyList<TradeBook> holding, CancellationToken cancellationToken)
    {
        if (!position.OneWay || Owner(accountId, position.Symbol, null, holding) is not { } owner || owner.Trade.State is not (TradeState.Open or TradeState.Closing))
        {
            return null;
        }

        DateTimeOffset now = time.GetUtcNow();
        StopRequest? stop = null;
        await trades.ChangeAsync(owner.Trade.Id, worker, book =>
        {
            Trade trade = book.Trade;
            if (trade.State is not (TradeState.Open or TradeState.Closing))
            {
                return false;
            }

            if (position.Size == 0 || position.Side is null)
            {
                if (trade.Numbers.Held > 0 && trade.FlatSince is null)
                {
                    trade.FlatSince = now;
                    return true;
                }

                return false;
            }

            bool changed = trade.FlatSince is not null;
            trade.FlatSince = null;
            if (position.Side != trade.Side)
            {
                trade.Fail($"позиция на бирже другой стороны — {TradeTexts.Price(position.Size)} против {TradeTexts.Price(trade.Numbers.Held)}");
                logger.LogWarning("Trade {Trade} on {Symbol} is in error ({Source}): {Reason}", trade.Id, trade.Symbol, source, trade.Reason);
                return true;
            }

            if (position.Size > trade.Numbers.Held && trade.Numbers.Entered >= trade.Quantity)
            {
                trade.Fail($"позиция на бирже больше сделки — {TradeTexts.Price(position.Size)} против {TradeTexts.Price(trade.Numbers.Held)}");
                logger.LogWarning("Trade {Trade} on {Symbol} is in error ({Source}): {Reason}", trade.Id, trade.Symbol, source, trade.Reason);
                return true;
            }

            // The stop stays on the exchange while there is a position: in «Открыта» and in «Закрывается» alike.
            if (trade.State is TradeState.Open or TradeState.Closing)
            {
                bool farther = position.Stop is not { } placed || (trade.Stop - placed) * (int)trade.Side > 0;
                if (farther)
                {
                    stop = new StopRequest(trade.Id, trade.Symbol, trade.Stop);
                }
                else if (trade.StopFailures > 0)
                {
                    trade.StopFailures = 0;
                    changed = true;
                }
            }

            return changed;
        }, null, cancellationToken);
        return stop;
    }

    private static void Move(TradeBook book, int number, TradeOrderStatus status, string? exchangeId)
    {
        if (book.Orders.FirstOrDefault(order => order.Number == number) is { } order && order.CanMoveTo(status))
        {
            book.Put(order with { Status = status, ExchangeOrderId = exchangeId ?? order.ExchangeOrderId });
        }
    }

    // The trade that holds the pair on the account; for a fill of an order of the bench, the trade of the order.
    private static TradeBook? Owner(Guid accountId, string symbol, string? linkId, IReadOnlyList<TradeBook> holding)
    {
        if (TradeOrderId.Parse(linkId) is { } link)
        {
            // A late fill of an order of a trade that no longer holds its pair is not that of another trade.
            return holding.FirstOrDefault(book => book.Trade.Id == link.TradeId);
        }

        return holding.FirstOrDefault(book => book.Trade.AccountId == accountId && book.Trade.Symbol == symbol);
    }
}
