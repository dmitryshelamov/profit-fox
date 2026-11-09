namespace ProfitFox.Domain.Entities.Ideas.Enums;

/// <summary>The band a reward to risk falls into.</summary>
public static class RiskRewardBands
{
    public static RiskRewardBand Of(decimal riskReward)
    {
        return riskReward switch
        {
            < 2m => RiskRewardBand.UpTo2,
            < 3m => RiskRewardBand.From2To3,
            < 6m => RiskRewardBand.From3To6,
            _ => RiskRewardBand.From6
        };
    }
}