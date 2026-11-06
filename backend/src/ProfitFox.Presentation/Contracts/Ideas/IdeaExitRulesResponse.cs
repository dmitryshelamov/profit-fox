using System.ComponentModel.DataAnnotations;

using ProfitFox.Application.Ideas.Queries.GetExitRules;
using ProfitFox.Application.MarketData;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>The exit rules of every type × interval and the lifetimes and windows each interval takes.</summary>
public sealed record IdeaExitRulesResponse([property: Required] IReadOnlyList<IdeaExitRuleResponse> Rules, [property: Required] IReadOnlyList<ExitRuleGridResponse> Grids)
{
    public static IdeaExitRulesResponse Of(GetExitRulesResult result)
    {
        return new IdeaExitRulesResponse(
            [.. result.Rules.Select(IdeaExitRuleResponse.Of)],
            [.. result.Grids.Select(grid => new ExitRuleGridResponse(grid.Key.ToCode(), grid.Value))]);
    }
}

/// <summary>The candles of the grid of an interval, up to 7 days.</summary>
public sealed record ExitRuleGridResponse([property: Required] string Interval, [property: Required] IReadOnlyList<int> Candles);