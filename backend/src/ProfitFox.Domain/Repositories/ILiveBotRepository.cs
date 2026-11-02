using ProfitFox.Domain.Entities.LiveTrading;
using ProfitFox.Domain.ValueObjects.LiveTrading;

namespace ProfitFox.Domain.Repositories;

/// <summary>
///     Stores live bots with their positions, orders and fills. The methods marked "in a unit of work" write in its transaction;
///     the others open a connection of their own.
/// </summary>
public interface ILiveBotRepository
{
    /// <summary>A bot that never started reads as a new stopped one.</summary>
    Task<LiveBot> GetAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>The bots that ever started; the others are stopped.</summary>
    Task<IReadOnlyDictionary<Guid, LiveBot>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Takes a bot a Worker must keep and nobody keeps, and grows its lease epoch; <c>null</c> when there is none.</summary>
    Task<LeasedBot?> ClaimAsync(Guid owner, DateTimeOffset now, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>The bot as it is now, or <c>null</c> when the lease of this take is lost.</summary>
    Task<LiveBot?> RenewAsync(Guid botId, Guid owner, long epoch, DateTimeOffset leasedUntil, CancellationToken cancellationToken);

    /// <summary>Gives the bot up, if this take still holds it.</summary>
    Task ReleaseAsync(Guid botId, Guid owner, long epoch, CancellationToken cancellationToken);

    /// <summary>In a unit of work: locks the bot and its live row for a change of the owner, creating the row of a new bot.</summary>
    /// <returns><c>null</c> when there is no such bot.</returns>
    Task<LiveBot?> LockAsync(Guid botId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>In a unit of work: locks the live row while this take holds it, or returns <c>null</c>.</summary>
    Task<LiveBot?> LockLeasedAsync(Guid botId, Guid owner, long epoch, CancellationToken cancellationToken);

    /// <summary>In a unit of work: writes a locked bot.</summary>
    Task SaveAsync(LiveBot bot, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>In a unit of work: the position with the highest number, or <c>null</c> before the first one.</summary>
    Task<LivePosition?> GetCurrentPositionAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>In a unit of work: the realized PnL of every closed position.</summary>
    Task<decimal> GetPastPnlAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>In a unit of work.</summary>
    Task SavePositionAsync(LivePosition position, CancellationToken cancellationToken);

    /// <summary>In a unit of work: orders not done yet, by number.</summary>
    Task<IReadOnlyList<LiveOrder>> GetUnfinishedOrdersAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>In a unit of work: adds or replaces the order of its number.</summary>
    Task SaveOrderAsync(LiveOrder order, CancellationToken cancellationToken);

    /// <summary>In a unit of work: <c>false</c> and nothing written when the exchange's id of the fill is stored already.</summary>
    Task<bool> AddFillAsync(LiveFill fill, CancellationToken cancellationToken);

    /// <summary>Every position of the bot, the newest first.</summary>
    Task<IReadOnlyList<LivePosition>> GetPositionsAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>Every order of the bot, by number.</summary>
    Task<IReadOnlyList<LiveOrder>> GetOrdersAsync(Guid botId, CancellationToken cancellationToken);

    /// <summary>Every fill of the bot, by time.</summary>
    Task<IReadOnlyList<LiveFill>> GetFillsAsync(Guid botId, CancellationToken cancellationToken);
}