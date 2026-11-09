namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>A band of the reward to risk of the plan of «Уровни» (decision 4 of add-own-idea-numbers); the lower bound is in the band.</summary>
public enum RiskRewardBand
{
    /// <summary>Below 2.</summary>
    UpTo2,

    /// <summary>From 2 to 3.</summary>
    From2To3,

    /// <summary>From 3 to 6.</summary>
    From3To6,

    /// <summary>From 6.</summary>
    From6
}