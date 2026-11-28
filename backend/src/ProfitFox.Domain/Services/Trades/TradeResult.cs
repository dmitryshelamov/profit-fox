using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.Services.Trades;

/// <summary>
///     The numbers of a trade from its fills (design.md, decision 6): what the entry bought or sold, what is held now, the
///     mean prices of the entry and of the exits, the fees of the exchange, the funding paid to the account, and the outcome
///     <c>(the value of the exits − the value of the entry at its mean price) × the sign of the side − fees + funding</c>.
///     For a trade closed whole it is <c>(exit value − entry value) × sign − fees + funding</c>.
/// </summary>
public sealed record TradeResult(
    decimal Entered,
    decimal Held,
    decimal? EntryPrice,
    decimal? ExitPrice,
    decimal Fees,
    decimal Funding,
    decimal Result,
    bool Increased,
    TradeFill? LastExit)
{
    public static readonly TradeResult None = new TradeResult(0m, 0m, null, null, 0m, 0m, 0m, false, null);

    private const string Snapshot = "snapshot:";

    /// <summary>
    ///     The id of the entry a trade gets from the snapshot of its position when the fills of its entry do not come (review
    ///     of the application, N4); the fills of the entry, once they come, take its place.
    /// </summary>
    public static string SnapshotId(Guid tradeId)
    {
        return Snapshot + tradeId.ToString("N");
    }

    /// <summary>
    ///     The numbers of a trade of <paramref name="side" /> from its fills, in the order of their time.
    /// </summary>
    public static TradeResult Of(TradeSide side, IEnumerable<TradeFill> fills)
    {
        decimal entered = 0m, entryValue = 0m, exited = 0m, exitValue = 0m, fees = 0m, funding = 0m;
        bool increased = false;
        TradeFill? lastExit = null;
        IReadOnlyCollection<TradeFill> all = [.. fills];
        bool real = all.Any(fill => fill.Kind == FillKind.Entry && !fill.ExecId.StartsWith(Snapshot, StringComparison.Ordinal));
        fills = real ? all.Where(fill => !fill.ExecId.StartsWith(Snapshot, StringComparison.Ordinal)) : all;
        foreach (TradeFill fill in fills.OrderBy(fill => fill.Time).ThenBy(fill => fill.ExecId, StringComparer.Ordinal))
        {
            switch (fill.Kind)
            {
                case FillKind.Funding:
                    funding -= fill.Fee;
                    continue;
                case FillKind.Entry:
                    entered += fill.Quantity;
                    entryValue += fill.Quantity * fill.Price;
                    break;
                case FillKind.Exit:
                    exited += fill.Quantity;
                    exitValue += fill.Quantity * fill.Price;
                    lastExit = fill;
                    break;
                case FillKind.Increase:
                    increased = true;
                    break;
            }

            fees += fill.Fee;
        }

        decimal? entryPrice = entered > 0 ? entryValue / entered : null;
        decimal? exitPrice = exited > 0 ? exitValue / exited : null;
        decimal realised = entryPrice is { } mean ? (exitValue - exited * mean) * (int)side : 0m;
        return new TradeResult(entered, Math.Max(0m, entered - exited), entryPrice, exitPrice, fees, funding, realised - fees + funding, increased, lastExit);
    }

    /// <summary>
    ///     The result of an open position now at <paramref name="mark" />: <c>(mark − mean entry) × held × sign</c>.
    /// </summary>
    public decimal Unrealised(TradeSide side, decimal mark)
    {
        return EntryPrice is { } entry ? (mark - entry) * Held * (int)side : 0m;
    }
}
