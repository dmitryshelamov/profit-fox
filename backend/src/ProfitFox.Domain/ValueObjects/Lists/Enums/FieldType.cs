namespace ProfitFox.Domain.ValueObjects.Lists.Enums;

/// <summary>
///     The type of a field of a list (change add-ideas-and-market, design.md, decision 10): a number, a text, one of a set of
///     values, yes or no, or a moment.
/// </summary>
public enum FieldType
{
    Number,
    Text,
    Choice,
    Boolean,
    Time
}
