using ProfitFox.Domain.Entities.Market.Enums;
using ProfitFox.Domain.ValueObjects.Market;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Repositories;

/// <summary>Stores the liquidation events of linear pairs as the stream gives them, with no end date.</summary>
public interface ILiquidationRepository
{
    /// <summary>Inserts the events of several instruments in one batch; an event storage already has stays a single row.</summary>
    /// <returns>The number of rows inserted of each side; a side with none is absent.</returns>
    Task<IReadOnlyDictionary<LiquidationSide, int>> AddAsync(IReadOnlyCollection<(Instrument Instrument, LiquidationEvent Event)> events, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="limit" /> stored events with time in <c>[from, to)</c>, oldest first.</summary>
    Task<IReadOnlyList<LiquidationEvent>> GetAsync(Instrument instrument, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken cancellationToken);

    /// <summary>The time of the latest stored event of any instrument, none while storage has no event.</summary>
    Task<DateTimeOffset?> LastTimeAsync(CancellationToken cancellationToken);
}