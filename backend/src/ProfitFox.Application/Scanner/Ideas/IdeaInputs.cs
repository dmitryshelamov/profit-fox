using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>
///     What a minute of a pair brings beside its candle: the spread now (none in a replay of history), the percentiles of volatility and the
///     price step of the pair, unknown until the list of instruments tells it; the change of the close of <c>linear</c> / <c>BTCUSDT</c> over the
///     240 minutes up to the same minute and the last funding rate of the pair at it or before (decision 2 of add-market-idea-features), none
///     where it is not known and the rate none on spot.
/// </summary>
public sealed record IdeaInputs(
    decimal? Spread,
    IReadOnlyDictionary<Interval, VolatilityPercentiles> Percentiles,
    decimal? Tick,
    decimal? BtcChange = null,
    decimal? Funding = null)
{
    public static readonly IdeaInputs None = new IdeaInputs(null, new Dictionary<Interval, VolatilityPercentiles>(), null);
}