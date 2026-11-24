using ProfitFox.Domain.ValueObjects.Lists.Enums;

namespace ProfitFox.Domain.ValueObjects.Lists;

/// <summary>
///     A field of a list or of a filter (change add-ideas-and-market, design.md, decisions 10 and 11): its name in requests,
///     its title, its type, the unit a condition writes its value in — <c>%</c> for a fraction, <c>млн USDT</c> for millions —
///     and, for a choice, its values. How a number in the unit becomes the stored one is <see cref="Scale" />.
/// </summary>
public sealed record ListField(string Name, string Title, FieldType Type, FieldPlace Place, string? Unit = null, IReadOnlyList<string>? Values = null)
{
    public const string Percent = "%";
    public const string Millions = "млн USDT";

    /// <summary>
    ///     What a value in the unit is multiplied by to give the stored value: 0.01 for a percent, a million for millions.
    /// </summary>
    public decimal Scale => Unit switch
    {
        Percent => 0.01m,
        Millions => 1_000_000m,
        _ => 1m
    };
}
