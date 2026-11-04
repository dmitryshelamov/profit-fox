using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the funding settlements of linear pairs.</summary>
public interface IFundingRateRepository
{
    /// <summary>Inserts the points or replaces the stored ones of the same time.</summary>
    Task UpsertAsync(Instrument instrument, IReadOnlyCollection<FundingRatePoint> points, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="limit" /> stored points with time in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<FundingRatePoint>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);
}