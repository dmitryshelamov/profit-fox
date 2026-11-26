namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The numbers of a set or of a pair of a set as a backtest keeps and gives them (spec strategy-backtest «Результат
///     набора»): the orders, the closed trades and the pairs with them, the mean result of a trade, the sum in USDT, the
///     share of trades above zero, the orders without a trade by reason, the skipped orders, the open cycles with their
///     estimate, and the difference from random entries. A set of a category of ideas also has its numbers by weeks
///     (<see cref="Weekly" />, change fix-backtest-random-control); a pair of a set has none. A number that a set does not
///     have is null.
/// </summary>
public sealed record SetSummary(
    int Orders,
    int Trades,
    int PairsWithTrades,
    double? Mean,
    double Amount,
    double? WinShare,
    Exclusions Excluded,
    int Skipped,
    int OpenCycles,
    double OpenAmount,
    RandomDifference? Difference)
{
    /// <summary>
    ///     The numbers of the set by weeks; none for a pair, for a set without trades and for a strategy for a bot.
    /// </summary>
    public WeeklyNumbers? Weekly { get; init; }

    /// <summary>
    ///     The numbers of a set, with its numbers by weeks.
    /// </summary>
    public static SetSummary OfSet(SetNumbers numbers)
    {
        return Of(numbers) with { Weekly = WeeklyNumbers.Of(numbers.Weeks) };
    }

    /// <summary>
    ///     The numbers of a pair of a set: without numbers by weeks.
    /// </summary>
    public static SetSummary Of(SetNumbers numbers)
    {
        return new SetSummary(
            numbers.Orders,
            numbers.Trades,
            numbers.PairsWithTrades,
            numbers.Mean,
            numbers.Amount,
            numbers.WinShare,
            numbers.Excluded,
            numbers.Skipped,
            numbers.OpenCycles,
            numbers.OpenAmount,
            numbers.Difference());
    }
}