using MediatR;

using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     A page of the chart of a pair (change add-ideas-and-market, design.md, decision 9; spec market-overview «График пары»,
///     «OI и фандинг под графиком»): the closed candles of the timeframe before <see cref="Before" /> — up to now when not
///     given — and the funding payments over the same time. The open interest comes by its own query (task 19.7).
/// </summary>
public sealed record GetPairChartQuery(string Symbol, string? Timeframe, DateTimeOffset? Before) : IRequest<PairChartInternal?>;

/// <summary>
///     The page of the chart, or that the service is still loading the history of the pair; null for a pair outside the set.
/// </summary>
public sealed record PairChartInternal(
    TimeSpan Timeframe,
    bool Loading,
    IReadOnlyList<HistoryCandle> Candles,
    IReadOnlyList<HistoryPayment> Funding);
