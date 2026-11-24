namespace ProfitFox.Presentation.Contracts.Market;

/// <summary>
///     A page of the chart of a pair (spec market-overview «График пары», «OI и фандинг под графиком»): the closed candles of
///     the timeframe and the funding payments with their rates as fractions, all in milliseconds; <see cref="Loading" />
///     while the service loads the history of the pair. The open interest is <see cref="PairOpenInterestResponse" />.
/// </summary>
public sealed record PairChartResponse(
    string Symbol,
    string Timeframe,
    bool Loading,
    IReadOnlyList<ChartCandle> Candles,
    IReadOnlyList<ChartPoint> Funding);

/// <summary>
///     The open interest of a page of the chart of a pair (task 19.7): in coins, one point a candle at its open, in
///     milliseconds; <see cref="Loading" /> while the service loads it.
/// </summary>
public sealed record PairOpenInterestResponse(string Symbol, string Timeframe, bool Loading, IReadOnlyList<ChartPoint> OpenInterest);

public sealed record ChartCandle(long OpenTime, double Open, double High, double Low, double Close, double Volume);

public sealed record ChartPoint(long Time, double Value);

/// <summary>
///     A live candle of the page of a pair (spec market-overview «Живая свеча»), sent by the stream.
/// </summary>
public sealed record LiveCandleResponse(long OpenTime, double Open, double High, double Low, double Close, double Volume, bool Closed, long UpdatedAt);
