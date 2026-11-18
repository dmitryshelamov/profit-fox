namespace ProfitFox.Engine.Simulation.Market;

/// <summary>
///     A closed minute candle of a pair; <paramref name="OpenTime" /> is a whole minute (UTC). A candle of volume 0 is a minute without
///     trades.
/// </summary>
public sealed record MinuteCandle(DateTimeOffset OpenTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);