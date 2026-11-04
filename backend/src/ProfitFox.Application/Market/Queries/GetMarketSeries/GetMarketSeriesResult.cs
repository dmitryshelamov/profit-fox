namespace ProfitFox.Application.Market.Queries.GetMarketSeries;

/// <summary>
///     The points oldest first, or validation errors keyed by parameter name; <see cref="NotFound" /> for a spot pair, which has neither
///     series.
/// </summary>
public sealed record GetMarketSeriesResult(IReadOnlyList<(DateTimeOffset Time, decimal Value)>? Points, bool NotFound, IReadOnlyDictionary<string, string> Errors);