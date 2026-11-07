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

    /// <summary>On <c>linear</c> the stop is at least half the way to the liquidation at the leverage of the entry.</summary>
    StopTooFarForLeverage,

    /// <summary>On <c>linear</c> the account has a position or open orders on the symbol, and the leverage of the symbol is not the one of the entry.</summary>
    LeverageDiffers,

    /// <summary>The exchange did not set the leverage on the symbol before the entry.</summary>
    LeverageNotSet,

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