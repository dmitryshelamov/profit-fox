namespace ProfitFox.Domain.ValueObjects.Backtests;

/// <summary>
///     Where a backtest is (change add-strategy-backtest, design.md, decision 2): waiting, going, or ended with numbers,
///     with an error, or by its owner.
/// </summary>
public static class BacktestState
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
