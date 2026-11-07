using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Application.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The out-of-sample check of a candidate exit rule: the bounds of the selection and the check, which end at the horizon of the longest rule of
///     the grid before now, the rules of the grid, the clustered numbers
///     of the candidate and of the best rule of the selection on both parts (none without an entry), and the probability of overfitting of the
///     grid with its splits. Numbers are decimal strings.
/// </summary>
public sealed record ExitRuleOutOfSampleResponse(
    DateTimeOffset SelectionFrom,
    DateTimeOffset CheckFrom,
    DateTimeOffset To,
    int Rules,
    [property: Required] ExitRulePartsResponse Candidate,
    ExitRuleBestResponse? Best,
    OverfittingResponse? Overfitting)
{
    public static ExitRuleOutOfSampleResponse Of(ExitRuleOutOfSample check)
    {
        return new ExitRuleOutOfSampleResponse(
            check.Window.From,
            check.Window.CheckFrom,
            check.Window.To,
            check.Rules,
            ExitRulePartsResponse.Of(check.Candidate),
            check.Best is { } best
                ? new ExitRuleBestResponse(
                    best.Rule.LifeCandles,
                    best.Rule.TargetR?.ToString(CultureInfo.InvariantCulture),
                    best.Parts.SelectionTo,
                    BucketStatisticsResponse.Of(best.Parts.Selection),
                    BucketStatisticsResponse.Of(best.Parts.Check))
                : null,
            check.Overfitting is { } overfitting
                ? new OverfittingResponse(overfitting.Splits, overfitting.Overfit, overfitting.Probability.ToString(CultureInfo.InvariantCulture))
                : null);
    }
}

/// <summary>The numbers of a rule on the selection, which ends the horizon of the rule before the check, and on the check.</summary>
public sealed record ExitRulePartsResponse(DateTimeOffset SelectionTo, BucketStatisticsResponse? Selection, BucketStatisticsResponse? Check)
{
    public static ExitRulePartsResponse Of(ExitRuleParts parts)
    {
        return new ExitRulePartsResponse(parts.SelectionTo, BucketStatisticsResponse.Of(parts.Selection), BucketStatisticsResponse.Of(parts.Check));
    }
}

/// <summary>
///     The best rule of the grid on the selection: its lifetime in candles, its target in R (none for «Уровни») and its numbers, the selection up
///     to the horizon of the rule before the check.
/// </summary>
public sealed record ExitRuleBestResponse(int Life, string? Target, DateTimeOffset SelectionTo, BucketStatisticsResponse? Selection, BucketStatisticsResponse? Check);

/// <summary>Of the splits that counted, the overfit ones and their share.</summary>
public sealed record OverfittingResponse(int Splits, int Overfit, [property: Required] string Probability);