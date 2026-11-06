namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>Why a trade by an idea may not be entered.</summary>
public enum IdeaTradeRefusal
{
    IdeaNotOpen,
    PriceBeyondStop,
    PriceBeyondTarget,
    RiskRewardTooLow,
    TooManyOpenTrades,
    TotalRiskExceeded,
    OppositeSideOpen,
    NotEnoughMoney,
    QuantityBelowMinimum,
    AmountBelowMinimum,
    NoKey,
    CategoryNotTraded,
    HedgeMode,
    SpotNotSupported,
    StopEntryNotSupported
}