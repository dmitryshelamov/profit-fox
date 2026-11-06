using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Application.MarketData;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The exit rule of a type × interval: the lifetime in candles, the target in R as a decimal string of a type with one, the entry window in
///     candles of a pullback and a stop entry, and when it was applied, none for the default.
/// </summary>
public sealed record IdeaExitRuleResponse([property: Required] string Type, [property: Required] string Interval, int Life, string? Target, int? Window, DateTimeOffset? AppliedAt)
{
    public static IdeaExitRuleResponse Of(IdeaExitRule rule)
    {
        return new IdeaExitRuleResponse(
            rule.Type.ToCode(),
            rule.Interval.ToCode(),
            rule.LifeCandles,
            rule.TargetR?.ToString(CultureInfo.InvariantCulture),
            rule.EntryCandles,
            rule.AppliedAt);
    }
}