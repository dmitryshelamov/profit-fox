using ProfitFox.Domain.ValueObjects.Trades.Enums;

namespace ProfitFox.Domain.ValueObjects.Trades;

/// <summary>
///     When a trade ends, without its result: whether it counts, and the moment of its exit — null for a trade left out.
///     This is what may be known of an event before its outcome may be read.
/// </summary>
public sealed record TradeExit(TradeStatus Status, DateTimeOffset? Time);
