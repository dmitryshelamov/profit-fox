using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     What a minute of a pair brings beside its candle: the spread now (none in a replay of history), the percentiles of volatility and the
///     price step of the pair, unknown until the list of instruments tells it.
/// </summary>
public sealed record IdeaInputs(decimal? Spread, IReadOnlyDictionary<Interval, VolatilityPercentiles> Percentiles, decimal? Tick)
{
    public static readonly IdeaInputs None = new IdeaInputs(null, new Dictionary<Interval, VolatilityPercentiles>(), null);
}