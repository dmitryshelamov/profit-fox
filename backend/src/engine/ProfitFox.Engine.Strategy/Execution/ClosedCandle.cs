namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     A closed candle of the timeframe of the graph: the instrument and the open and close times (UTC). The nodes of today read no
///     prices; they come with the node of the candle.
/// </summary>
public sealed record ClosedCandle(string InstrumentId, DateTimeOffset OpenTime, DateTimeOffset CloseTime);