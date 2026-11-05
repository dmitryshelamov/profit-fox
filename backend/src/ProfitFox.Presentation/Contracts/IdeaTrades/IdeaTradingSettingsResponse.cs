using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Domain.Entities.IdeaTrades;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>The settings of trading by ideas; numbers are decimal strings, the account is none until the owner chose one.</summary>
public sealed record IdeaTradingSettingsResponse(
    Guid? AccountId,
    [property: Required] string Risk,
    [property: Required] string MaxAmount,
    int MaxOpenTrades,
    [property: Required] string MaxTotalRisk,
    [property: Required] string MinRiskReward)
{
    public static IdeaTradingSettingsResponse Of(IdeaTradingSettings settings)
    {
        return new IdeaTradingSettingsResponse(
            settings.AccountId,
            settings.Risk.ToString(CultureInfo.InvariantCulture),
            settings.MaxAmount.ToString(CultureInfo.InvariantCulture),
            settings.MaxOpenTrades,
            settings.MaxTotalRisk.ToString(CultureInfo.InvariantCulture),
            settings.MinRiskReward.ToString(CultureInfo.InvariantCulture));
    }
}