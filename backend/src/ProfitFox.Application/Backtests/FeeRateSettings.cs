namespace ProfitFox.Application.Backtests;

/// <summary>Fee rates of limit and market orders in percent, and the slippage in percent the ideas count on fills that do not rest on the book.</summary>
public sealed class FeeRateSettings
{
    public decimal Limit { get; init; }

    public decimal Market { get; init; }

    public decimal Slippage { get; init; }
}