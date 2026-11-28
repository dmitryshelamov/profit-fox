using ProfitFox.Domain.Exceptions;
using ProfitFox.Domain.ValueObjects.Trades;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     The emergency stop (change take-idea-trades, design.md, decision 11).
/// </summary>
public interface ITradeHaltRepository
{
    Task<TradeHalt> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Sets the emergency stop off in one transaction under the lease of <paramref name="worker" />: the row of the ban with
    ///     the moment, the loss and the limit; a close order «стоп-кран» for every open trade, which then closes; the trades
    ///     whose entry order was never tried cancelled — one tried may be at the exchange, and the trading Worker asks the exchange
    ///     before it gives it up. Under the advisory lock of the entry. False when the ban already holds: it goes off once.
    /// </summary>
    /// <exception cref="LeaseLostException">The Worker does not hold the lease; nothing is written.</exception>
    Task<bool> HaltAsync(string worker, decimal loss, decimal limit, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    ///     Lifts the ban at <paramref name="at" />; false when a trade still closes, and nothing is written then.
    /// </summary>
    Task<bool> LiftAsync(DateTimeOffset at, CancellationToken cancellationToken);
}
