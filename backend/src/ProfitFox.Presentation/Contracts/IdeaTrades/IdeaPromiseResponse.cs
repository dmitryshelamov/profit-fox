using System.ComponentModel.DataAnnotations;

using ProfitFox.Application.Ideas;
using ProfitFox.Domain.ValueObjects.IdeaTrades;

namespace ProfitFox.Presentation.Contracts.IdeaTrades;

/// <summary>
///     What the idea promised when the trade was entered: the badge of its bucket (<c>up</c>, <c>flat</c> or <c>down</c>), the probability, the
///     expectancy in R and the cases of the bucket over 90 days (the badge and the probability none for «мало данных», all four none without a
///     bucket), and the break-even and the reward to risk after the fees from the entry price. Numbers are decimal strings.
/// </summary>
public sealed record IdeaPromiseResponse(string? Badge, string? Probability, string? Expectancy, int? Cases, string? BreakEven, [property: Required] string NetRiskReward)
{
    public static IdeaPromiseResponse? Of(IdeaPromise? promise)
    {
        return promise is null
            ? null
            : new IdeaPromiseResponse(
                promise.Badge?.ToCode(),
                promise.Probability is { } probability ? IdeaTradeResponse.Text(probability) : null,
                promise.Expectancy is { } expectancy ? IdeaTradeResponse.Text(expectancy) : null,
                promise.Cases,
                promise.BreakEven is { } breakEven ? IdeaTradeResponse.Text(breakEven) : null,
                IdeaTradeResponse.Text(promise.NetRiskReward));
    }
}