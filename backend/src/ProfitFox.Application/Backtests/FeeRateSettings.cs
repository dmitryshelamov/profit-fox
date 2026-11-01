namespace ProfitFox.Application.Backtests;

/// <summary>Fee rates of limit and market orders in percent.</summary>
public sealed class FeeRateSettings
{
    public decimal Limit { get; init; }

    public decimal Market { get; init; }
}