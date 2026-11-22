namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     The numbers of a set or of a pair of a set as a backtest keeps and gives them (spec strategy-backtest «Результат
///     набора»): the orders, the closed trades and the pairs with them, the mean result of a trade, the sum in USDT, the
///     share of trades above zero, the orders without a trade by reason, the skipped orders, the open cycles with their
///     estimate, and the difference from random entries. A number that a set does not have is null.
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
