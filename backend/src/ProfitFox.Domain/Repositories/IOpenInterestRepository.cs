using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the 5 minute points of open interest of linear pairs, in coins.</summary>
public interface IOpenInterestRepository
{
    /// <summary>Inserts the points or replaces the stored ones of the same time.</summary>
    Task UpsertAsync(Instrument instrument, IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken);

    /// <summary>Inserts the points whose time storage does not have yet; a stored point stays as it is.</summary>
    Task AddAsync(Instrument instrument, IReadOnlyCollection<OpenInterestPoint> points, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="limit" /> stored points with time in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<OpenInterestPoint>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);
}