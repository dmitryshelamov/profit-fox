namespace ProfitFox.Domain.Entities.IdeaTrades.Enums;

/// <summary>Why a trade by an idea may not be entered. The ones from <see cref="DailyStop" /> on come to an auto only: a manual entry never gets them.</summary>
public enum IdeaTradeRefusal
{
    IdeaNotOpen,
    PriceBeyondStop,
    PriceBeyondTarget,
    RiskRewardTooLow,
    AmountAboveMax,
    TooManyOpenTrades,
    TotalRiskExceeded,
    OppositeSideOpen,
    NotEnoughMoney,
    QuantityBelowMinimum,
    AmountBelowMinimum,
    NoKey,
    CategoryNotTraded,
    HedgeMode,
    StopEntryNotSupported,

    DailyStop,
    TooManySameDirection,
    PairTaken,
    Late,
    AutoOff,
    RulesUnknown,

    /// <summary>The entry of an auto could not be prepared for another reason than a silent exchange, such as a pair the exchange no longer lists.</summary>
    EntryError,

    /// <summary>The open trades of the auto, real and paper, with the amount of this entry would be above the budget of the auto.</summary>
    BudgetTaken,

    /// <summary>The open real trades of all the autos with the amount of this entry would be above the budget of all the autos.</summary>
    AutosBudgetTaken
}