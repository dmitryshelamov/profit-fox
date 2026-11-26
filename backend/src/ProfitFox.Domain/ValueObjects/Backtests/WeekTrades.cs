namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The sums of the trades of a set in one week (change fix-backtest-random-control, design.md, decision 9): the results
///     after costs as fractions of what was put in, the number of trades and the results in USDT. A trade goes to the week of
///     the moment of its order; a week starts on Monday 00:00 UTC.
/// </summary>
public readonly record struct WeekTrades(long Week, double NetSum, int Trades, double Amount)
{
    /// <summary>
    ///     The mean result of a trade of the week after costs.
    /// </summary>
    public double Mean => NetSum / Trades;
}