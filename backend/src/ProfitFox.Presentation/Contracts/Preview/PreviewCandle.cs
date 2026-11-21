namespace ProfitFox.Presentation.Contracts.Preview;

/// <summary>
///     A candle with trades for the chart: its open time in Unix milliseconds UTC, prices and volume as numbers.
/// </summary>
public sealed record PreviewCandle(long OpenTime, double Open, double High, double Low, double Close, double Volume);
