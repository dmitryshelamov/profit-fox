using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Application.Market.Queries.GetMarketPair;

/// <summary>The row, or none when the snapshot has no such pair, or validation errors keyed by parameter name.</summary>
public sealed record GetMarketPairResult(MarketSnapshotRow? Row, IReadOnlyDictionary<string, string> Errors);