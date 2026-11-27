namespace ProfitFox.Domain.ValueObjects.Execution;

/// <summary>
///     A closed candle of the timeframe a graph runs on. Its moment is <see cref="CloseTime" />. <see cref="BuyVolume" /> is
///     the volume of the trades the buyer started (change add-idea-score, design.md, decision 7); null when it is not known
///     for a minute of the candle with trades.
/// </summary>
public sealed record ClosedCandle(
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    decimal? BuyVolume = null);
