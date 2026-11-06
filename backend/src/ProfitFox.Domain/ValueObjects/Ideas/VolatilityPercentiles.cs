namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>The 10th and 90th percentiles of ATR / price of a pair on an interval: «Сжатие» stays below the low one, «Высокая волатильность» exceeds the high one.</summary>
public sealed record VolatilityPercentiles(decimal? Low, decimal? High)
{
    public static readonly VolatilityPercentiles None = new VolatilityPercentiles(null, null);
}