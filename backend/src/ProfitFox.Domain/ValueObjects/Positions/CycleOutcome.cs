using ProfitFox.Domain.ValueObjects.Graph.Enums;
using ProfitFox.Domain.ValueObjects.Trades;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Positions;

/// <summary>
///     The outcome of a cycle (spec strategy-position «Результат цикла», «Доля цикла по входам»): its status, side and
///     fills, the number of entries, the largest money put in, how it closed, whether the order of fills in a minute was
///     ambiguous, and the result before costs, the costs and the result after them as fractions of the largest money put
///     in; <see cref="NetAmount" /> is that result in USDT. <see cref="R" /> is given only to a cycle of one entry with a
///     stop. An <see cref="Open" /> cycle did not close by the end of the computed history: its numbers are an estimate at
///     the last close, without the fee of an exit. A cycle left out has only its status and side.
/// </summary>
public sealed record CycleOutcome(
    TradeStatus Status,
    EntrySide Side,
    IReadOnlyList<CycleFill> Fills,
    int Entries,
    double MaxInvested,
    CycleExit ExitReason,
    bool Ambiguous,
    double? Gross,
    double? Fees,
    double? Slippage,
    double? Funding,
    double? Net,
    double? NetAmount,
    double? R,
    bool Open)
{
    public static CycleOutcome Excluded(TradeStatus status, EntrySide side)
    {
        return new CycleOutcome(status, side, [], 0, 0, CycleExit.None, Ambiguous: false, null, null, null, null, null, null, null, Open: false);
    }

    /// <summary>
    ///     The cycle of a trade of a plan of one entry of <paramref name="amount" /> USDT by <paramref name="node" />, as the
    ///     bench counted it: the entry and the exit at the prices of the trade, its fractions, the result in USDT the fraction
    ///     times the amount. A trade left out gives a cycle left out.
    /// </summary>
    public static CycleOutcome FromTrade(TradeOutcome trade, EntrySide side, double amount, string? node)
    {
        if (trade.Status != TradeStatus.Ok)
        {
            return Excluded(trade.Status, side);
        }

        bool isLong = side == EntrySide.Long;
        double entryPrice = trade.EntryPrice!.Value;
        double quantity = amount / entryPrice;
        double exitPrice = trade.ExitPrice!.Value;
        CycleFill[] fills =
        [
            new CycleFill(trade.EntryTime!.Value, FillAction.Entry, isLong ? FillSide.Buy : FillSide.Sell, entryPrice, quantity, amount, node),
            new CycleFill(trade.ExitTime!.Value, FillAction.Exit, isLong ? FillSide.Sell : FillSide.Buy, exitPrice, quantity, quantity * exitPrice, Node: null)
        ];
        CycleExit exit = trade.ExitReason switch
        {
            Trades.Enums.ExitReason.Stop => CycleExit.Stop,
            Trades.Enums.ExitReason.Target => CycleExit.Target,
            _ => CycleExit.Time
        };
        return new CycleOutcome(
            TradeStatus.Ok,
            side,
            fills,
            Entries: 1,
            amount,
            exit,
            trade.Ambiguous,
            trade.Gross,
            trade.Fees,
            trade.Slippage,
            trade.Funding,
            trade.Net,
            trade.Net * amount,
            trade.R,
            Open: false);
    }

    /// <summary>
    ///     The cycle as the trade of a plan (spec trade-plan-outcome): the entry is its first fill, the exit its last. This
    ///     is what the bench, its journal and its report read.
    /// </summary>
    public TradeOutcome ToTrade()
    {
        if (Status != TradeStatus.Ok)
        {
            return TradeOutcome.Excluded(Status);
        }

        CycleFill entry = Fills[0];
        CycleFill exit = Fills[^1];
        return new TradeOutcome(
            Status,
            ExitReason switch
            {
                CycleExit.Stop => Trades.Enums.ExitReason.Stop,
                CycleExit.Target => Trades.Enums.ExitReason.Target,
                CycleExit.Time => Trades.Enums.ExitReason.Time,
                _ => throw new InvalidOperationException($"A trade of a plan does not end by {ExitReason}.")
            },
            Ambiguous,
            entry.Moment,
            exit.Moment,
            entry.Price,
            exit.Price,
            Gross,
            Fees,
            Slippage,
            Funding,
            Net,
            R);
    }
}
