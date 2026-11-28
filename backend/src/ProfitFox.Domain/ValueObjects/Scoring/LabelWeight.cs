namespace ProfitFox.Domain.ValueObjects.Scoring;

/// <summary>
///     The weights of a label (change add-idea-score, spec idea-score «Вес метки»; design.md, decision 1): by the text of the
///     label, as an idea keeps its labels, one for a long and one for a short, numbers from <see cref="Min" /> to
///     <see cref="Max" /> with at most two digits after the point (change decimal-label-weights, design.md, decision 2). A
///     text without weights weighs 0 on either side.
/// </summary>
public sealed record LabelWeight(string Text, decimal Long, decimal Short)
{
    public const decimal Min = -100;
    public const decimal Max = 100;

    /// <summary>
    ///     Whether <paramref name="weight" /> is a weight: a number from <see cref="Min" /> to <see cref="Max" /> without digits
    ///     other than noughts past the hundredths.
    /// </summary>
    public static bool Valid(decimal weight)
    {
        return weight == decimal.Round(weight, 2) && weight is >= Min and <= Max;
    }
}
