using ProfitFox.Domain.Entities.Trades;
using ProfitFox.Domain.Entities.Trades.Enums;
using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Lists;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Where the trades by ideas are kept with their orders and fills (change take-idea-trades, design.md, decision 12). A
///     change is one operation whole in its transaction — the entry, a change of a trade —, not «read, change, save». A
///     change of the trading Worker names it: the transaction first takes the lease of the trader under <c>FOR UPDATE</c> and
///     throws <see cref="LeaseLostException" /> when the Worker does not hold it; the API changes without it.
/// </summary>
public interface ITradeRepository
{
    /// <summary>
    ///     Writes the trade and its entry order in one transaction under the advisory lock of the entry, after the limits are
    ///     counted anew under it: the ban, the number of the trades that hold their pairs, the sum of their amounts and a trade
    ///     on the pair of the account. Nothing is written when one of them refuses.
    /// </summary>
    Task<EntryRefusal?> OpenAsync(Trade trade, TradeOrder entry, int maxOpenTrades, decimal maxTotalAmount, CancellationToken cancellationToken);

    /// <summary>
    ///     The counters of an entry: the trades that hold their pairs, the sum of their amounts, and whether one holds the pair
    ///     on the account.
    /// </summary>
    Task<(int Open, decimal Amount, bool PairTaken)> CountersAsync(Guid? accountId, string symbol, CancellationToken cancellationToken);

    Task<Trade?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    ///     Every trade that holds its pair, with its orders.
    /// </summary>
    Task<IReadOnlyList<TradeBook>> HoldingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TradeFill>> FillsAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>
    ///     Changes the trade of <paramref name="id" /> in one transaction: reads it under its lock with its orders and fills,
    ///     gives it to <paramref name="change" /> and writes back the trade, every order and the new fills. A fill is written
    ///     once by its id. <paramref name="change" /> returns false to write nothing. Null when there is no trade.
    /// </summary>
    /// <param name="id">The trade.</param>
    /// <param name="worker">The trading Worker that changes it, or null for the API.</param>
    /// <param name="change">The change.</param>
    /// <param name="probe">The ids of executions to tell in <see cref="TradeBook.Known" /> whether the store keeps them.</param>
    /// <param name="cancellationToken">The request was given up.</param>
    /// <exception cref="LeaseLostException">The Worker does not hold the lease; nothing is written.</exception>
    Task<TradeBook?> ChangeAsync(Guid id, string? worker, Func<TradeBook, bool> change, IReadOnlyCollection<string>? probe, CancellationToken cancellationToken);

    /// <summary>
    ///     Writes the result of the open trades now with the moment, under the lease of the trading Worker.
    /// </summary>
    /// <exception cref="LeaseLostException">The Worker does not hold the lease; nothing is written.</exception>
    Task MarkAsync(string worker, IReadOnlyDictionary<Guid, decimal> unrealised, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    ///     The latest trade of each idea of <paramref name="ideaIds" /> that was not cancelled.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, (Guid TradeId, TradeState State)>> ByIdeasAsync(IReadOnlyCollection<Guid> ideaIds, CancellationToken cancellationToken);

    Task<ListPage<TradeListItem>> ListAsync(TradeListQuery query, CancellationToken cancellationToken);

    /// <summary>
    ///     How many trades hold their pairs and how many are closed or cancelled, and over the closed ones the sum of their
    ///     outcomes — net of fees, with funding — and the sum of their fees.
    /// </summary>
    Task<(int Open, int Closed, decimal Result, decimal Fees)> CountsAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     The outcomes of the trades closed from <paramref name="from" />, and the results now of the trades that hold their pairs.
    /// </summary>
    Task<(IReadOnlyList<(DateTimeOffset ClosedAt, decimal Result)> Closed, IReadOnlyList<decimal> Open)> DayAsync(DateTimeOffset from, CancellationToken cancellationToken);

    /// <summary>
    ///     Whether a trade of the account holds its pair.
    /// </summary>
    Task<bool> HoldsAnyAsync(Guid accountId, CancellationToken cancellationToken);
}
