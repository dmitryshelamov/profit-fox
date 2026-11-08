using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the border of the coverage of a series of candles (category, symbol, interval).</summary>
public interface ISeriesCoverageRepository
{
    /// <summary>The coverage of the series, or <c>null</c> when nothing was ever covered.</summary>
    Task<SeriesCoverage?> GetAsync(Instrument instrument, Interval interval, CancellationToken cancellationToken);

    /// <summary>Writes the coverage of the series, replacing the one stored.</summary>
    Task UpsertAsync(Instrument instrument, Interval interval, SeriesCoverage coverage, CancellationToken cancellationToken);
}