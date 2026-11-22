using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     Reads the minute candles of a pair opened in [<paramref name="from" />, <paramref name="to" />) with their coverage.
/// </summary>
public delegate Task<HistoryRead<HistoryCandle>> MinuteReader(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
