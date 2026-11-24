using MediatR;

using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Application.Queries.Market;

/// <summary>
///     The open interest of a page of the chart of a pair (change add-ideas-and-market, task 19.7; spec market-overview «OI и
///     фандинг под графиком»): over the window of the page of <see cref="GetPairChartQuery" /> with the same
///     <see cref="Before" />, the last point of each candle. Its own query: a long window of open interest is slow to read,
///     and the candles and the payments do not wait for it.
/// </summary>
public sealed record GetPairOpenInterestQuery(string Symbol, string? Timeframe, DateTimeOffset? Before) : IRequest<PairOpenInterestInternal?>;

/// <summary>
///     The open interest of the page, or that the service is still loading it; null for a pair outside the set.
/// </summary>
public sealed record PairOpenInterestInternal(TimeSpan Timeframe, bool Loading, IReadOnlyList<OpenInterestPoint> OpenInterest);
