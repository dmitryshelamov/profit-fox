using ProfitFox.Domain.Entities.MarketData;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

public interface ICandleRepository
{
    /// <summary>Inserts the candles or replaces the stored ones with the same open time.</summary>
    Task UpsertAsync(Instrument instrument, Interval interval, IReadOnlyCollection<Candle> candles, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="limit"/> stored candles with open time in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<Candle>> GetAsync(
        Instrument instrument, Interval interval, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);
}
