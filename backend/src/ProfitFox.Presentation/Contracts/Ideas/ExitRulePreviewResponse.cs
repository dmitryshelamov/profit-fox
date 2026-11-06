using System.ComponentModel.DataAnnotations;
using System.Globalization;

using ProfitFox.Application.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The preview of a candidate exit rule: the statistics by the rule now and by the candidate over 90 and 30 days (none without an entry), the
///     expectancy at every lifetime of the grid and the curve of MFE. Numbers are decimal strings.
/// </summary>
public sealed record ExitRulePreviewResponse(
    [property: Required] ExitRuleWindowResponse Last90,
    [property: Required] ExitRuleWindowResponse Last30,
    [property: Required] IReadOnlyList<ExitRuleLifeResponse> Lives,
    [property: Required] IReadOnlyList<ExitRuleMfeResponse> Mfe)
{
    public static ExitRulePreviewResponse Of(ExitRulePreview preview)
    {
        return new ExitRulePreviewResponse(
            ExitRuleWindowResponse.Of(preview.Last90),
            ExitRuleWindowResponse.Of(preview.Last30),
            [.. preview.Lives.Select(life => new ExitRuleLifeResponse(life.Candles, Text(life.Expectancy), Text(life.SignalExpectancy)))],
            [.. preview.Mfe.Select(point => new ExitRuleMfeResponse(Text(point.R)!, Text(point.Reached)!, Text(point.BreakEven)!))]);
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed record ExitRuleWindowResponse(BucketStatisticsResponse? Current, BucketStatisticsResponse? Candidate)
{
    public static ExitRuleWindowResponse Of(ExitRuleWindow window)
    {
        return new ExitRuleWindowResponse(BucketStatisticsResponse.Of(window.Current), BucketStatisticsResponse.Of(window.Candidate));
    }
}

/// <summary>The expectancy per entry and per signal at a lifetime in candles.</summary>
public sealed record ExitRuleLifeResponse(int Candles, string? Expectancy, string? SignalExpectancy);

/// <summary>The share of the entries that reached +R before the stop and the end of the lifetime, and the break-even of a target of R.</summary>
public sealed record ExitRuleMfeResponse([property: Required] string R, [property: Required] string Reached, [property: Required] string BreakEven);