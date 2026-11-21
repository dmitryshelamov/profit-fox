namespace ProfitFox.Domain.ValueObjects.Execution;

/// <summary>
///     A closed candle of the timeframe a graph runs on. Its moment is <see cref="CloseTime" />.
/// </summary>
public sealed record ClosedCandle(
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);
