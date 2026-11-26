using ProfitFox.Domain.ValueObjects.Backtests;
using ProfitFox.Domain.ValueObjects.MarketData;

namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     Reads the history of <paramref name="pair" /> for a pass of a backtest; <paramref name="again" /> is set for the second
///     pass of a category of ideas, which reads the candles and the funding of the pair a second time.
/// </summary>
public delegate Task<PairHistory> PairHistoryReader(Instrument pair, bool again, CancellationToken cancellationToken);