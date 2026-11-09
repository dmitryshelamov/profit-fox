using System.Globalization;

using ProfitFox.Application.Ideas;
using ProfitFox.Domain.ValueObjects.Ideas;

namespace ProfitFox.Presentation.Contracts.Ideas;

/// <summary>
///     The own numbers of an idea, which the list sorts and filters by in place of those of its bucket: the probability with its 95 % interval,
///     the probability of the bucket over 90 days times <see cref="Multiplier" />, the multiplier of the band of the reward to risk of the plan of
///     «Уровни» (<see cref="Band" />: <c>up_to_2</c>, <c>from_2_to_3</c>, <c>from_3_to_6</c>, <c>from_6</c>; for the other types 1 and none); the
///     expectancy in R with its interval, the expectancy of the bucket without costs less <see cref="Costs" />, the fees and the slippage of the
///     idea in R, and the badge by that interval, <c>up</c>, <c>flat</c> or <c>down</c>; and <see cref="ExpectancyPerUsdt" />, the expectancy on
///     1 USDT of the amount of a deal. What an idea does not have is none. Numbers are decimal strings, shares fractions.
/// </summary>
public sealed record IdeaNumbersResponse(
    string? Probability,
    string? ProbabilityLow,
    string? ProbabilityHigh,
    string Multiplier,
    string? Band,
    string? Expectancy,
    string? ExpectancyLow,
    string? ExpectancyHigh,
    string? Badge,
    string Costs,
    string? ExpectancyPerUsdt)
{
    public static IdeaNumbersResponse Of(IdeaNumbers numbers)
    {
        return new IdeaNumbersResponse(
            Text(numbers.Probability),
            Text(numbers.ProbabilityLow),
            Text(numbers.ProbabilityHigh),
            Text(numbers.Multiplier)!,
            numbers.Band?.ToCode(),
            Text(numbers.Expectancy),
            Text(numbers.ExpectancyLow),
            Text(numbers.ExpectancyHigh),
            numbers.Badge?.ToCode(),
            Text(numbers.Costs)!,
            Text(numbers.ExpectancyPerUsdt));
    }

    private static string? Text(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }
}