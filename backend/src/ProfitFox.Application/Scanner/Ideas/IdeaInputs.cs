using ProfitFox.Domain.Entities.MarketData.Enums;

namespace ProfitFox.Application.Scanner.Ideas;

/// <summary>What a minute of a pair brings beside its candle: the spread now (none in a replay of history) and the percentiles of volatility.</summary>
public sealed record IdeaInputs(decimal? Spread, IReadOnlyDictionary<Interval, decimal> Percentiles)
{
    public static readonly IdeaInputs None = new IdeaInputs(null, new Dictionary<Interval, decimal>());
}