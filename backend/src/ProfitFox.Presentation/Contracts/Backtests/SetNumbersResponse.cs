namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>
///     The numbers of a set or of a pair (spec strategy-backtest «Результат набора»): the orders, the closed trades and the
///     pairs with them, the mean result of a trade after costs as a fraction of what was put in, the sum in USDT, the share
///     of trades above zero, the orders without a trade by reason, the skipped orders, the open cycles with their estimate
///     in USDT, and the difference from random entries of a category of ideas. A set of a category of ideas of engine
///     version 5 and later also has its numbers by weeks; a pair of a set has none. A number the set does not have is null.
/// </summary>
public sealed record SetNumbersResponse(
    int Orders,
    int Trades,
    int PairsWithTrades,
    double? Mean,
    double Amount,
    double? WinShare,
    ExclusionsResponse Excluded,
    int Skipped,
    int OpenCycles,
    double OpenAmount,
    RandomDifferenceResponse? Difference,
    WeeklyNumbersResponse? Weekly = null);