using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the minutes of the trade flow of the pairs, with no end date; a minute written again replaces the stored one.</summary>
public interface ITradeFlowMinuteRepository
{
    /// <summary>Replaces the stored minutes of the batch while <paramref name="owner" /> holds the lease of the trade flow.</summary>
    /// <returns><c>false</c> when the owner no longer holds the lease; nothing is written then.</returns>
    Task<bool> SaveAsync(Guid owner, IReadOnlyCollection<(Instrument Instrument, TradeFlowMinute Minute)> minutes, CancellationToken cancellationToken);

    /// <summary>
    ///     Replaces every stored minute of <paramref name="instrument" /> in the day UTC that starts at <paramref name="day" /> with
    ///     <paramref name="minutes" />, in one transaction, while <paramref name="owner" /> holds the lease.
    /// </summary>
    /// <returns><c>false</c> when the owner no longer holds the lease; nothing is written then.</returns>
    Task<bool> ReplaceDayAsync(Guid owner, Instrument instrument, DateTimeOffset day, IReadOnlyCollection<TradeFlowMinute> minutes, CancellationToken cancellationToken);

    /// <summary>The bars of <paramref name="interval" /> in <c>[from, to)</c>, each the sum of its stored minutes; a bar without one is absent.</summary>
    Task<IReadOnlyList<TradeFlowMinute>> GetBarsAsync(Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The first stored minute of <paramref name="instrument" />, none without one.</summary>
    Task<DateTimeOffset?> FirstAsync(Instrument instrument, CancellationToken cancellationToken);

    /// <summary>The latest stored minute of any pair, none while storage has no minute.</summary>
    Task<DateTimeOffset?> LastAsync(CancellationToken cancellationToken);
}