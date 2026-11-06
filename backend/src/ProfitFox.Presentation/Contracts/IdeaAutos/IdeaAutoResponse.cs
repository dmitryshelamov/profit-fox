using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Application.IdeaAutos;
using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.Entities.IdeaAutos;
using ProfitFox.Domain.ValueObjects.IdeaAutos;

namespace ProfitFox.Presentation.Contracts.IdeaAutos;

/// <summary>
///     An auto with its account and its conditions as codes, its place in the list (from 0) and its mode (<c>off</c>, <c>paper</c> or <c>live</c>).
///     Numbers are decimal strings.
/// </summary>
public sealed record IdeaAutoResponse(
    Guid Id,
    [property: Required] string Name,
    Guid AccountId,
    [property: Required] string AccountName,
    [property: Required] string Amount,
    [property: Required] string Mode,
    [property: Required] IReadOnlyList<string> Setups,
    [property: Required] IReadOnlyList<string> Types,
    [property: Required] IReadOnlyList<string> Intervals,
    string? Side,
    string? Category,
    string? Badge,
    [property: Required] string MinRiskReward,
    int MinCases,
    int Position)
{
    public static IdeaAutoResponse Of(IdeaAutoSummary summary)
    {
        IdeaAuto auto = summary.Auto;
        IdeaAutoConditions conditions = auto.Conditions;

        return new IdeaAutoResponse(
            auto.Id,
            auto.Name,
            auto.AccountId,
            summary.AccountName,
            Text(auto.Amount),
            auto.Mode.ToCode(),
            [.. conditions.Setups.Select(setup => setup.ToCode())],
            [.. conditions.Types.Select(type => type.ToCode())],
            [.. conditions.Intervals.Select(interval => interval.ToCode())],
            conditions.Side?.ToCode(),
            conditions.Category?.ToCode(),
            conditions.Badge?.ToCode(),
            Text(conditions.MinRiskReward),
            conditions.MinCases,
            auto.Position);
    }

    internal static string Text(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}