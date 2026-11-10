using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores which days of which pairs the daily archive of trades was read for, and what it gave.</summary>
public interface ITradeFlowDayRepository
{
    /// <summary>Records the day, replacing what was stored for it.</summary>
    Task SaveAsync(TradeFlowDay day, CancellationToken cancellationToken);

    /// <summary>The recorded days of every pair with the day in <c>[from, to)</c>.</summary>
    Task<IReadOnlyList<TradeFlowDay>> GetAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>The recorded days of <paramref name="instrument" /> with the day in <c>[from, to)</c>.</summary>
    Task<IReadOnlyList<TradeFlowDay>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}