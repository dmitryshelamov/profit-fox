using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Application.IdeaTrades;
using ProfitFox.Presentation.Contracts.IdeaAutos;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>
///     The entry of an idea as it would be made now. Numbers are decimal strings: the price, the quantity, the amount, the loss at the stop and the profit at the target it
///     really takes at that quantity after the fees, the reward to the risk after the fees and without them, the free money of the account (none without a key), the leverage (one on the spot)
///     and the margin, the amount at the leverage. <see cref="Reasons" /> are the codes of what stops the entry, <see cref="Details" /> the numbers of
///     each by its code, none for a reason without; a reason named twice has the numbers it was named with first.
/// </summary>
public sealed record IdeaEntryResponse(
    Guid? AccountId,
    [property: Required] string EntryPrice,
    [property: Required] string Quantity,
    [property: Required] string Amount,
    [property: Required] string Risk,
    [property: Required] string Profit,
    [property: Required] string RiskReward,
    [property: Required] string RiskRewardWithoutFees,
    string? FreeMoney,
    [property: Required] string Quote,
    [property: Required] IReadOnlyList<string> Reasons,
    int Leverage,
    [property: Required] string Margin,
    [property: Required] IReadOnlyDictionary<string, RefusalDetailResponse?> Details)
{
    public static IdeaEntryResponse Of(EntryCalculation entry)
    {
        return new IdeaEntryResponse(
            entry.Preparation.Account?.Id,
            Text(entry.Preparation.Price),
            Text(entry.Size.Quantity),
            Text(entry.Size.Amount),
            Text(entry.Size.Risk),
            Text(entry.Size.Profit),
            Text(entry.Size.RiskReward),
            Text(entry.Plan.RiskReward),
            entry.FreeMoney is { } free ? Text(free) : null,
            entry.Preparation.Quote,
            [.. entry.Reasons.Select(IdeaTradeCodes.ToCode)],
            entry.Leverage,
            Text(entry.Margin),
            entry.Refusals.DistinctBy(refusal => refusal.Reason).ToDictionary(refusal => refusal.Reason.ToCode(), refusal => RefusalDetailResponse.Of(refusal.Detail)));
    }

    private static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}