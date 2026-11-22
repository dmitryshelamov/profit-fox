namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>
///     A page of the chart of a pair (spec market-overview «График пары», «OI и фандинг под графиком»): the closed candles of
///     the timeframe, the open interest in coins one point a candle, the funding payments with their rates as fractions, all
///     in milliseconds; <see cref="Loading" /> while the service loads the history of the pair.
/// </summary>
public sealed record PairChartResponse(
    string Symbol,
    string Timeframe,
    bool Loading,
    IReadOnlyList<ChartCandle> Candles,
    IReadOnlyList<ChartPoint> OpenInterest,
    IReadOnlyList<ChartPoint> Funding);

public sealed record ChartCandle(long OpenTime, double Open, double High, double Low, double Close, double Volume);

public sealed record ChartPoint(long Time, double Value);

/// <summary>
///     A live candle of the page of a pair (spec market-overview «Живая свеча»), sent by the stream.
/// </summary>
public sealed record LiveCandleResponse(long OpenTime, double Open, double High, double Low, double Close, double Volume, bool Closed, long UpdatedAt);
