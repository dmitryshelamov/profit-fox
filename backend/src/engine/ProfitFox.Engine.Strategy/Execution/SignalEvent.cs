namespace ProfitFox.Engine.Strategy.Execution;

/// <summary>
///     An event of a signal: the pair and the close of the candle it happened on.
/// </summary>
public sealed record SignalEvent(string Instrument, DateTimeOffset Moment);
