using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.ValueObjects.Market;

/// <summary>The row of a pair in the market snapshot: its metrics, when they were computed, and whether storage still misses some of its minutes.</summary>
public sealed record MarketSnapshotRow(Instrument Instrument, DateTimeOffset ComputedAt, PairMetrics Metrics, bool Incomplete);