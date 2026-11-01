namespace ProfitFox.Presentation.Contracts.Backtests;

/// <summary>Settings of the simulation; a missing one takes the balance of the bot, the base rates of Bybit, no slippage and breakout.</summary>
public sealed class SimulationBody
{
    public decimal? Balance { get; init; }

    public decimal? LimitFeePercent { get; init; }

    public decimal? MarketFeePercent { get; init; }

    public decimal? SlippagePercent { get; init; }

    /// <summary><c>touch</c> or <c>breakout</c>.</summary>
    public string? LimitFillMode { get; init; }
}