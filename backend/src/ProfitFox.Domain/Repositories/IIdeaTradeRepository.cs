using ProfitFox.Domain.Entities.IdeaTrades;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Stores trades by ideas with their orders, the stop and target orders the exchange made for them, and their fills. The methods
///     marked "in a unit of work" write in its transaction; the others open a connection of their own.
/// </summary>
public interface IIdeaTradeRepository
{
    Task<IdeaTrade?> GetAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>Every trade, the newest first.</summary>
    Task<IReadOnlyList<IdeaTrade>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>The trades that wait for their entry or are open, of every account.</summary>
    Task<IReadOnlyList<IdeaTrade>> GetOpenAsync(CancellationToken cancellationToken);

    /// <summary>Takes a trade a Worker must keep and nobody keeps, and grows its lease epoch; <c>null</c> when there is none.</summary>
    Task<LeasedIdeaTrade?> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Renews the lease of every trade this Worker holds by the epoch of its take, in one query.</summary>
    /// <returns>The renewed trades as they are now; a trade that is not there has lost its lease.</returns>
    Task<IReadOnlyList<LeasedIdeaTrade>> RenewAllAsync(Guid owner, IReadOnlyCollection<(Guid TradeId, long Epoch)> trades, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>The trades this Worker holds that were written after <paramref name="since" />.</summary>
    Task<IReadOnlyList<ChangedIdeaTrade>> GetChangedAsync(Guid owner, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Gives the trade up, if this take still holds it.</summary>
    Task ReleaseAsync(Guid tradeId, Guid owner, long epoch, CancellationToken cancellationToken);

    /// <summary>Gives up every trade this Worker still holds by the epoch of its take, in one query.</summary>
    Task ReleaseAllAsync(Guid owner, IReadOnlyCollection<(Guid TradeId, long Epoch)> trades, CancellationToken cancellationToken);

    /// <summary>In a unit of work: waits for the entry of any other trade and then holds the lock until the unit ends: the limits count the trades of every account.</summary>
    Task LockCreationAsync(CancellationToken cancellationToken);

    /// <summary>In a unit of work: saves a new trade with its entry order.</summary>
    Task CreateAsync(IdeaTrade trade, IdeaTradeOrder entry, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>In a unit of work: locks the row of a trade for a change by the API, or returns <c>null</c>.</summary>
    Task<IdeaTrade?> LockAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>In a unit of work: locks the row of a trade while this take holds it, or returns <c>null</c>.</summary>
    Task<IdeaTrade?> LockLeasedAsync(Guid tradeId, Guid owner, long epoch, CancellationToken cancellationToken);

    /// <summary>In a unit of work: writes a locked trade.</summary>
    Task SaveAsync(IdeaTrade trade, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>In a unit of work: orders not done yet, by number.</summary>
    Task<IReadOnlyList<IdeaTradeOrder>> GetUnfinishedOrdersAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>In a unit of work: adds or replaces the order of its number.</summary>
    Task SaveOrderAsync(IdeaTradeOrder order, CancellationToken cancellationToken);

    /// <summary>In a unit of work: adds or replaces the child order of its exchange id.</summary>
    Task SaveChildAsync(IdeaTradeChild child, CancellationToken cancellationToken);

    /// <summary>In a unit of work: <c>false</c> and nothing written when the exchange's id of the fill is stored already.</summary>
    Task<bool> AddFillAsync(IdeaTradeFill fill, CancellationToken cancellationToken);

    /// <summary>Whether the exchange's id is the stop or the target of a trade.</summary>
    Task<bool> IsChildAsync(string exchangeOrderId, CancellationToken cancellationToken);

    /// <summary>Every order of the trade, by number.</summary>
    Task<IReadOnlyList<IdeaTradeOrder>> GetOrdersAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>Every child order of the trade.</summary>
    Task<IReadOnlyList<IdeaTradeChild>> GetChildrenAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>Every fill of the trade, by time.</summary>
    Task<IReadOnlyList<IdeaTradeFill>> GetFillsAsync(Guid tradeId, CancellationToken cancellationToken);
}