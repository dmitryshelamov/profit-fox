using ProfitFox.Domain.ValueObjects.Market;

namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A row of the list of the market: the snapshot of the pair and its open ideas, newest first.
/// </summary>
public sealed record MarketPairRow(MarketPair Pair, IReadOnlyList<IdeaBadge> Ideas);

/// <summary>
///     An open idea as a row of the market shows it: its strategy, timeframe and side.
/// </summary>
public sealed record IdeaBadge(string Strategy, string Timeframe, string Side);
