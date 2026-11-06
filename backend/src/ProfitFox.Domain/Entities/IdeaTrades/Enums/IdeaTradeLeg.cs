namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>An order that takes a trade out of its position.</summary>
public enum IdeaTradeLeg
{
    TakeProfit,
    StopLoss,

    /// <summary>The market close the owner asked for.</summary>
    Manual
}