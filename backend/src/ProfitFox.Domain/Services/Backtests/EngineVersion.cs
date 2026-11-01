namespace ProfitFox.Domain.Services.Backtests;

/// <summary>
///     The version of the rules a backtest is computed by: the strategy engine, the simulator and the metrics. A Worker computes only
///     backtests of its own version, so one backtest is never computed by two sets of rules.
/// </summary>
public sealed record EngineVersion(int Number)
{
    /// <summary>Raised with every change that changes results; the reference test of the engine version fails until it is.</summary>
    public const int Current = 1;
}