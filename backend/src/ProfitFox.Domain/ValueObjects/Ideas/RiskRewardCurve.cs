using ProfitFox.Domain.Entities.Ideas.Enums;
using ProfitFox.Domain.Entities.MarketData.Enums;
using ProfitFox.Domain.Services.Ideas;

namespace ProfitFox.Domain.ValueObjects.Ideas;

/// <summary>
///     The curve of «Уровни» by the reward to risk (decision 4 of add-own-idea-numbers): the cases and the targets of each band of a set
///     interval × side × category, over all its setups and 90 days. The multiplier of a band is its own share of targets over that of the set,
///     without shrinking; a band of fewer than 30 cases takes that of the nearest band below with enough of them, 1 without one, and a set without
///     a target gives 1.
/// </summary>
public sealed record RiskRewardCurve(Interval Interval, IdeaSide Side, MarketCategory Category, IReadOnlyList<RiskRewardBandCount> Bands)
{
    /// <summary>The multiplier of the band of <paramref name="riskReward" />; 1 for a set without a curve.</summary>
    public static decimal MultiplierFor(RiskRewardCurve? curve, decimal riskReward)
    {
        return curve?.Multiplier(RiskRewardBands.Of(riskReward)) ?? 1m;
    }

    public decimal Multiplier(RiskRewardBand band)
    {
        int targets = Bands.Sum(count => count.Targets);

        if (targets == 0)
        {
            return 1m;
        }

        decimal share = (decimal)targets / Bands.Sum(count => count.Cases);

        for (RiskRewardBand below = band; below >= RiskRewardBand.UpTo2; below--)
        {
            if (Bands.FirstOrDefault(count => count.Band == below) is { Cases: >= BucketStatisticsCalculator.MinimumCases } enough)
            {
                return (decimal)enough.Targets / enough.Cases / share;
            }
        }

        return 1m;
    }
}

/// <summary>The cases of a band of a curve and the targets among them.</summary>
public sealed record RiskRewardBandCount(RiskRewardBand Band, int Cases, int Targets);