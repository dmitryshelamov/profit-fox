namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     An event of the signal: the close of the candle it happened on, in Unix milliseconds UTC.
/// </summary>
public sealed record PreviewEvent(long Moment);
