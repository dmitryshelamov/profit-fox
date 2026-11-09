namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>An order that takes a trade out of its position.</summary>
public enum IdeaTradeLeg
{
    TakeProfit,
    StopLoss,

    /// <summary>The market close the owner asked for.</summary>
    Manual,

    /// <summary>The close of a paper trade at the exit price of its idea when the idea expires.</summary>
    Expiry,

    /// <summary>The exchange liquidated the position, wholly or in part.</summary>
    Liquidation,

    /// <summary>The exchange closed the position by auto-deleveraging, wholly or in part.</summary>
    Adl
}