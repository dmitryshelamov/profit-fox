using ProfitFox.Application.Contracts.Preview;
using ProfitFox.Application.Exceptions;
using ProfitFox.Application.Queries.Filters;

namespace ProfitFox.Tests.Unit.Application.Queries.Filters;

/// <summary>
///     Change decimal-label-weights, task 2.5 (design.md, decision 3): «балл от» is a number with at most two digits after the
///     point, read in the invariant culture; none when not given; another value refuses the request at <c>minScore</c> (spec
///     idea-score «Дробный порог», «Отрицательный порог», «Не число»).
/// </summary>
public sealed class MinScoreTests
{
    [TestCase("2", 2)]
    [TestCase("1.25", 1.25)]
    [TestCase("-1", -1)]
    [TestCase("-0.5", -0.5)]
    [TestCase(" 0.10 ", 0.1)]
    public void A_number_with_at_most_two_digits_after_the_point_is_the_least_score(string text, double score)
    {
        Assert.That(FilterResolver.MinScore(text), Is.EqualTo((decimal)score));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void No_text_is_no_least_score(string? text)
    {
        Assert.That(FilterResolver.MinScore(text), Is.Null);
    }

    [TestCase("abc")]
    [TestCase("0.125")]
    [TestCase("1,5")]
    [TestCase("1e2")]
    public void Another_text_is_refused_at_the_least_score(string text)
    {
        IdeasRequestException refused = Assert.Throws<IdeasRequestException>(() => FilterResolver.MinScore(text))!;

        Assert.That(
            refused.Errors.Select(error => (error.Code, error.Field, error.Reason)),
            Is.EqualTo(new[] { ("InvalidMinScore", (string?)"minScore", "балл от — число, не больше двух знаков после точки") }));
        Assert.That(FilterResolver.MinScoreError(text, out decimal? score), Is.InstanceOf<PreviewErrorInternal>());
        Assert.That(score, Is.Null);
    }
}
