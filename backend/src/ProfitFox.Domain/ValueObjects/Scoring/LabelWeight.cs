namespace ProfitFox.Domain.ValueObjects.Scoring;

/// <summary>
///     The weights of a label (change add-idea-score, spec idea-score «Вес метки»; design.md, decision 1): by the text of the
///     label, as an idea keeps its labels, one for a long and one for a short, whole numbers from <see cref="Min" /> to
///     <see cref="Max" />. A text without weights weighs 0 on either side.
/// </summary>
public sealed record LabelWeight(string Text, int Long, int Short)
{
    public const int Min = -3;
    public const int Max = 3;

    /// <summary>
    ///     Whether <paramref name="weight" /> is a weight: a whole number from <see cref="Min" /> to <see cref="Max" />.
    /// </summary>
    public static bool Valid(decimal weight)
    {
        return weight == decimal.Truncate(weight) && weight is >= Min and <= Max;
    }
}
