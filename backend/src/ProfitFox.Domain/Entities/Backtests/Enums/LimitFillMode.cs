namespace ProfitFox.Domain.Entities.Backtests.Enums;

/// <summary>When a limit order fills in a backtest: as soon as the price touches it, or only when the price goes beyond it.</summary>
public enum LimitFillMode
{
    Touch,
    Breakout
}