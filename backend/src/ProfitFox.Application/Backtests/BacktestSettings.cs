namespace ProfitFox.Application.Backtests;

/// <summary>The <c>Backtests</c> section of the service settings.</summary>
public sealed class BacktestSettings
{
    /// <summary>Threads one backtest computes combinations on; without it, every core but one, at least one.</summary>
    public int? MaxParallelism { get; init; }

    public int Threads => MaxParallelism ?? Math.Max(Environment.ProcessorCount - 1, 1);
}